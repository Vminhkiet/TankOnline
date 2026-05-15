#include "Core/MatchManager.hpp"
#include "Network/NetworkManager.hpp"
#include "Utils/Logger.hpp"
#include <csignal>
#include <iostream>
#include <string>
#include <cstdlib>
#include <thread>
#include <sstream>
#include <timeapi.h>

#define ASIO_STANDALONE
#include <asio.hpp>
#include <nlohmann/json.hpp>
using json = nlohmann::json;

static std::string getEnv(const char* key, const char* def) {
    const char* v = std::getenv(key);
    return v ? v : def;
}

static std::atomic<bool> g_running{true};
static void onSignal(int) { g_running = false; }

// ── HTTP helpers ──────────────────────────────────────────────────────────────

static std::string httpOk(const std::string& body) {
    std::ostringstream ss;
    ss << "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
       << "Content-Length: " << body.size() << "\r\nConnection: close\r\n\r\n" << body;
    return ss.str();
}
static std::string httpBad(const std::string& msg) {
    std::string body = "{\"error\":\"" + msg + "\"}";
    std::ostringstream ss;
    ss << "HTTP/1.1 400 Bad Request\r\nContent-Type: application/json\r\n"
       << "Content-Length: " << body.size() << "\r\nConnection: close\r\n\r\n" << body;
    return ss.str();
}

// ── HTTP session handler ──────────────────────────────────────────────────────
// Fix: read the ENTIRE request (headers + body) into one buffer before parsing.
// This avoids ASIO partial-read bugs and handles both Content-Length
// and Transfer-Encoding: chunked from Spring RestTemplate.

static void handleSession(asio::ip::tcp::socket sock, MatchManager& manager) {
    try {
        asio::streambuf buf;
        asio::error_code ec;

        // 1. Read until end of headers (\r\n\r\n)
        asio::read_until(sock, buf, "\r\n\r\n", ec);
        if (ec && ec != asio::error::eof) return;

        // 2. Extract everything read so far into a string
        std::string raw(asio::buffers_begin(buf.data()), asio::buffers_end(buf.data()));
        buf.consume(buf.size());

        // 3. Split headers and partial body already in buffer
        size_t headerEnd = raw.find("\r\n\r\n");
        if (headerEnd == std::string::npos) return;

        std::string headers  = raw.substr(0, headerEnd);
        std::string body     = raw.substr(headerEnd + 4);

        // 4. Parse first request line
        size_t firstNl    = headers.find("\r\n");
        std::string reqLine = headers.substr(0, firstNl);

        bool isPost    = reqLine.find("POST /internal/match/create") != std::string::npos;
        bool isMetrics = reqLine.find("GET /metrics") != std::string::npos;

        if (isMetrics) {
            std::string resp = httpOk("{\"status\":\"online\"}");
            asio::write(sock, asio::buffer(resp), ec);
            return;
        }

        if (!isPost) {
            std::string resp = "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n";
            asio::write(sock, asio::buffer(resp), ec);
            return;
        }

        // 5. Read remaining body bytes
        //    Case A: Content-Length header present
        size_t clPos = headers.find("Content-Length:");
        if (clPos != std::string::npos) {
            size_t clEnd   = headers.find("\r\n", clPos);
            int contentLen = std::stoi(headers.substr(clPos + 15, clEnd - clPos - 15));
            int remaining  = contentLen - (int)body.size();
            if (remaining > 0) {
                std::vector<char> more(remaining);
                asio::read(sock, asio::buffer(more), ec);
                body.append(more.data(), more.size());
            }
        } else {
            // Case B: Transfer-Encoding: chunked — read until connection closes
            asio::error_code readEc;
            while (!readEc) {
                std::array<char, 4096> tmp;
                size_t n = sock.read_some(asio::buffer(tmp), readEc);
                if (n > 0) body.append(tmp.data(), n);
            }
            // Decode chunked: each chunk is "HEX\r\nDATA\r\n", ends with "0\r\n\r\n"
            // Simple decode: collect all non-hex-size lines
            std::string decoded;
            std::istringstream ss(body);
            std::string line;
            bool expectData = false;
            while (std::getline(ss, line)) {
                if (!line.empty() && line.back() == '\r') line.pop_back();
                if (!expectData) {
                    // This line is a chunk size in hex — skip it
                    expectData = true;
                } else {
                    // This line is chunk data
                    if (!line.empty()) decoded += line;
                    expectData = false;
                }
            }
            body = decoded;
        }

        // 6. Parse JSON and create match
        try {
            if (body.empty()) {
                LOG_ERR("mgmt: empty body received");
                std::string resp = httpBad("empty body");
                asio::write(sock, asio::buffer(resp), ec);
                return;
            }

            auto j = json::parse(body);
            MatchConfig cfg;
            cfg.matchId         = j.at("matchId").get<uint32_t>();
            cfg.mapName         = j.value("mapName", "world");
            cfg.maxDurationSecs = j.value("maxDurationSecs", 300);
            for (auto& pid : j.at("playerIds"))
                cfg.playerIds.push_back(pid.get<uint32_t>());

            manager.createMatch(std::move(cfg));
            LOG_INFO("mgmt: match created via HTTP (matchId={})", cfg.matchId);

            std::string resp = httpOk("{\"status\":\"ok\"}");
            asio::write(sock, asio::buffer(resp), ec);

        } catch (const std::exception& e) {
            LOG_ERR("mgmt: JSON parse error — {}", e.what());
            std::string resp = httpBad(e.what());
            asio::write(sock, asio::buffer(resp), ec);
        }

    } catch (...) {}
}

static void startMgmtServer(MatchManager& manager, int port) {
    try {
        asio::io_context ioc;
        asio::ip::tcp::acceptor acceptor(
            ioc,
            asio::ip::tcp::endpoint(asio::ip::tcp::v4(), port)
        );
        LOG_INFO("mgmt: HTTP server listening on :{}", port);

        while (g_running) {
            asio::ip::tcp::socket sock(ioc);
            asio::error_code ec;
            acceptor.accept(sock, ec);
            if (ec) continue;

            std::thread([s = std::move(sock), &manager]() mutable {
                handleSession(std::move(s), manager);
            }).detach();
        }
    } catch (const std::exception& e) {
        LOG_ERR("mgmt: server error — {}", e.what());
    }
}

// ─────────────────────────────────────────────────────────────────────────────

int main() {
    timeBeginPeriod(1);

    std::cout << "=========================================\n"
              << "        SERVER-TANK  v0.3 (match mode)  \n"
              << "=========================================\n\n";

    std::signal(SIGINT,  onSignal);
    std::signal(SIGTERM, onSignal);

    Logger::getInstance().init("server.log");

    const int udpPort  = std::stoi(getEnv("UDP_PORT",  "8080"));
    const int mgmtPort = std::stoi(getEnv("MGMT_PORT", "9090"));

    NetworkManager network;
    if (!network.start(udpPort)) {
        LOG_ERR("main: failed to bind UDP port {}", udpPort);
        return 1;
    }

    MatchManager manager(network);
    manager.start("");

    std::thread mgmtThread([&manager, mgmtPort]() {
        startMgmtServer(manager, mgmtPort);
    });
    mgmtThread.detach();

#ifdef TANK_DEV_HARDCODE_MATCH
    {
        MatchConfig cfg;
        cfg.matchId         = 1;
        cfg.mapName         = "world";
        cfg.maxDurationSecs = 600;
        cfg.playerIds       = {1, 2};
        manager.createMatch(std::move(cfg));
        LOG_INFO("main: DEV test match (matchId=1, players=[1,2])");
    }
#endif

    LOG_INFO("main: UDP={} MGMT_HTTP={}", udpPort, mgmtPort);

    while (g_running)
        std::this_thread::sleep_for(std::chrono::milliseconds(100));

    LOG_INFO("main: shutting down");
    manager.stop();
    network.stop();
    timeEndPeriod(1);
    return 0;
}
