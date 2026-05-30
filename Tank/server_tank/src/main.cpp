#include "Core/MatchScheduler.hpp"
#include "Network/INetworkBackend.hpp"
#include "Network/NetworkManager.hpp"
#include "Network/BlockingBackend.hpp"
#include "Utils/Logger.hpp"
#include <csignal>
#include <iostream>
#include <fstream>
#include <string>
#include <cstdlib>
#include <timeapi.h>   // timeBeginPeriod / timeEndPeriod (winmm)
#include <winsock2.h>
#include <ws2tcpip.h>

#include "Kafka/KafkaConsumer.hpp"
#include <nlohmann/json.hpp>
using json = nlohmann::json;

static std::string getEnv(const char* key, const char* def) {
    const char* v = std::getenv(key);
    return v ? v : def;
}

static std::atomic<bool> g_running{true};
static void onSignal(int) { g_running = false; }

// ── UDP Broadcast Discovery Listener ────────────────────────────────────────
// Listens on DISCOVERY_PORT for "TANK_DISCOVER" broadcasts from LAN clients.
// Replies with "TANK_SERVER:{gamePort}" so clients can auto-detect server IP.
static constexpr int DISCOVERY_PORT = 8888;
static constexpr const char* DISCOVER_MSG = "TANK_DISCOVER";

static void discoveryThread(int gamePort) {
    SOCKET sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (sock == INVALID_SOCKET) {
        LOG_ERR("[Discovery] Failed to create socket: {}", WSAGetLastError());
        return;
    }

    // Allow address reuse
    int optval = 1;
    setsockopt(sock, SOL_SOCKET, SO_REUSEADDR, (const char*)&optval, sizeof(optval));

    // Enable broadcast
    setsockopt(sock, SOL_SOCKET, SO_BROADCAST, (const char*)&optval, sizeof(optval));

    sockaddr_in bindAddr{};
    bindAddr.sin_family      = AF_INET;
    bindAddr.sin_port        = htons(DISCOVERY_PORT);
    bindAddr.sin_addr.s_addr = INADDR_ANY;

    if (bind(sock, (sockaddr*)&bindAddr, sizeof(bindAddr)) == SOCKET_ERROR) {
        LOG_ERR("[Discovery] Failed to bind port {}: {}", DISCOVERY_PORT, WSAGetLastError());
        closesocket(sock);
        return;
    }

    LOG_INFO("[Discovery] Listening on UDP port {} for LAN broadcast", DISCOVERY_PORT);

    // Set receive timeout so we can check g_running periodically
    DWORD timeout = 1000; // 1 second
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));

    char buf[256];
    while (g_running) {
        sockaddr_in clientAddr{};
        int addrLen = sizeof(clientAddr);
        int n = recvfrom(sock, buf, sizeof(buf) - 1, 0, (sockaddr*)&clientAddr, &addrLen);

        if (n <= 0) continue; // timeout or error
        buf[n] = '\0';

        if (std::string(buf, n) == DISCOVER_MSG) {
            char clientIp[INET_ADDRSTRLEN];
            inet_ntop(AF_INET, &clientAddr.sin_addr, clientIp, sizeof(clientIp));
            LOG_INFO("[Discovery] Received TANK_DISCOVER from {}:{}", clientIp, ntohs(clientAddr.sin_port));

            std::string reply = "TANK_SERVER:" + std::to_string(gamePort);
            sendto(sock, reply.c_str(), (int)reply.size(), 0,
                   (sockaddr*)&clientAddr, sizeof(clientAddr));
        }
    }

    closesocket(sock);
    LOG_INFO("[Discovery] Stopped");
}

int main() {
    // Reduce Windows multimedia timer resolution from default 15 ms to 1 ms.
    // This makes sleep_for accurate to ~1 ms instead of ~15 ms, preventing the
    // tick loop from running at ~64 Hz instead of 60 Hz.
    timeBeginPeriod(1);

    std::cout << "=========================================\n"
              << "  SERVER-TANK  v0.3 (Matchmaking ACK)   \n"
              << "=========================================\n\n";

    std::signal(SIGINT,  onSignal);
    std::signal(SIGTERM, onSignal);

    const std::string logFile = getEnv("LOG_FILE", "server.log");
    Logger::getInstance().init(logFile);

    const int udpPort = std::stoi(getEnv("UDP_PORT", "8080"));

    // Kafka broker list — set KAFKA_BROKERS=host:9092 to enable publishing.
    // Leave empty (default) to run without Kafka (stub silently no-ops).
    const std::string kafkaBrokers = getEnv("KAFKA_BROKERS", "localhost:9092");
    const std::string kafkaGroupId = getEnv("KAFKA_GROUP_ID", "tank-server");
    const std::string kafkaTopic   = getEnv("KAFKA_TOPIC_IN",  "match.create");
    const std::string kafkaSessionInvalidatedTopic = getEnv("KAFKA_TOPIC_SESSION_INVALIDATED", "user.session.invalidated");

    // ── Network backend (switchable via BACKEND env var) ────────────────────
    // BACKEND=iocp     → Windows IOCP, hardware_concurrency*2 worker threads (default)
    // BACKEND=blocking → Dedicated blocking-recvfrom threads (baseline comparison)
    const std::string backendEnv = getEnv("BACKEND", "iocp");
    const int blockingReceivers  = std::stoi(getEnv("BLOCKING_RECEIVERS", "2"));

    std::unique_ptr<INetworkBackend> netPtr;
    if (backendEnv == "blocking") {
        netPtr = std::make_unique<BlockingBackend>(blockingReceivers);
        std::cout << "  Backend  : Blocking-recvfrom (" << blockingReceivers << " receiver threads)\n\n";
    } else {
        netPtr = std::make_unique<NetworkManager>();
        std::cout << "  Backend  : IOCP (hardware_concurrency * 2 workers)\n\n";
    }

    if (!netPtr->start(udpPort)) {
        LOG_ERR("main: failed to bind UDP port {}", udpPort);
        return 1;
    }

    // Start LAN discovery broadcast listener
    std::thread discThread(discoveryThread, udpPort);
    discThread.detach();

    // ── Match manager ────────────────────────────────────────────────────────
    MatchScheduler manager(*netPtr);
    manager.start(kafkaBrokers);

    // ── Debug matches: load from debug_matches.json if present ─────────────
    // Format: [{"matchId":1,"players":[1,2],"mapName":"world","maxDuration":600}, ...]
    // If file not found, fall back to NUM_MATCHES auto-generated matches.
    bool debugFileLoaded = false;
    {
        std::ifstream f("debug_matches.json");
        if (f.good()) {
            try {
                auto arr = json::parse(f);
                for (auto& entry : arr) {
                    MatchConfig cfg;
                    cfg.matchId         = entry.at("matchId").get<uint32_t>();
                    cfg.mapName         = entry.value("mapName", "world");
                    cfg.maxDurationSecs = entry.value("maxDuration", 600);
                    for (auto& pid : entry.at("players"))
                        cfg.playerIds.push_back(pid.get<uint32_t>());
                    manager.createMatch(std::move(cfg));
                    LOG_INFO("main: [debug] match {} created (players: {})", cfg.matchId, cfg.playerIds.size());
                }
                LOG_INFO("main: loaded {} debug match(es) from debug_matches.json", arr.size());
                debugFileLoaded = true;
            } catch (const std::exception& e) {
                LOG_WARN("main: failed to parse debug_matches.json: {}", e.what());
            }
        }
    }
    if (!debugFileLoaded) {
        const int numMatches = std::stoi(getEnv("NUM_MATCHES", "0")); // Changed default to 0 to prevent filling up the server
        for (int m = 1; m <= numMatches; ++m) {
            MatchConfig cfg;
            cfg.matchId         = static_cast<uint32_t>(1002 + m);
            cfg.mapName         = "world";
            cfg.maxDurationSecs = 600;
            cfg.playerIds       = { static_cast<uint32_t>(2*m - 1),
                                     static_cast<uint32_t>(2*m) };
            manager.createMatch(std::move(cfg));
        }
        LOG_INFO("main: {} auto test match(es) created", numMatches);
    }

    KafkaConsumer consumer;
    bool kafkaOk = !kafkaBrokers.empty() &&
                   consumer.connect(kafkaBrokers, kafkaGroupId, {kafkaTopic, kafkaSessionInvalidatedTopic});
    if (!kafkaBrokers.empty() && !kafkaOk)
        LOG_WARN("main: Kafka unavailable — running without dynamic match creation");

    LOG_INFO("main: running (UDP={}, kafka={})", udpPort, kafkaOk ? "on" : "off");

    while (g_running) {
        if (!kafkaOk) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            continue;
        }
        bool ok = consumer.poll(500, [&](const KafkaMessage& msg) {
            try {
                auto j = json::parse(msg.payload);

                if (msg.topic == kafkaTopic) {
                    MatchConfig cfg;
                    cfg.matchId         = j.at("matchId").get<uint32_t>();
                    cfg.mapName         = j.value("mapName", "world");
                    cfg.maxDurationSecs = j.value("maxDuration", 300);
                    for (auto& pid : j.at("players"))
                        cfg.playerIds.push_back(pid.get<uint32_t>());
                    if (j.contains("userIds")) {
                        for (auto& [pidStr, uid] : j.at("userIds").items())
                            cfg.userIds[static_cast<uint32_t>(std::stoul(pidStr))] = uid.get<std::string>();
                    }
                    if (j.contains("tokens")) {
                        for (auto& [pidStr, tok] : j.at("tokens").items())
                            cfg.playerTokens[static_cast<uint32_t>(std::stoul(pidStr))] = tok.get<std::string>();
                    }
                    if (j.contains("tanks")) {
                        for (auto& [pidStr, tankData] : j.at("tanks").items()) {
                            TankStats stats;
                            stats.name = tankData.value("name", "BULLDOG");
                            stats.damage = tankData.value("damage", 25);
                            stats.armor = tankData.value("armor", 0);
                            stats.speed = tankData.value("speed", 12.0f);
                            stats.health = tankData.value("health", 100);
                            stats.fireRate = tankData.value("fireRate", 1.0f);
                            stats.fireRange = tankData.value("fireRange", 50.0f);
                            cfg.playerStats[static_cast<uint32_t>(std::stoul(pidStr))] = stats;
                        }
                    }
                    manager.createMatch(std::move(cfg));
                    return;
                }

                if (msg.topic == kafkaSessionInvalidatedTopic) {
                    const std::string userId = j.at("userId").is_string()
                        ? j.at("userId").get<std::string>()
                        : std::to_string(j.at("userId").get<long long>());
                    const uint16_t code = static_cast<uint16_t>(j.value("code", 1003));
                    const std::string message = j.value("message", std::string("Logged in from another device"));
                    const uint32_t disconnectAfterMs = 10000;
                    manager.forceLogoutByUserId(userId, code, message, disconnectAfterMs);
                    return;
                }

                LOG_WARN("main: received Kafka message from unexpected topic={}", msg.topic);
            } catch (const std::exception& e) {
                LOG_ERR("main: bad kafka payload topic={} err={}", msg.topic, e.what());
            }
        });
        if (!ok) { LOG_ERR("main: Kafka fatal error"); break; }
    }
    consumer.close();

    LOG_INFO("main: shutting down");
    manager.stop();
    netPtr->stop();
    timeEndPeriod(1);
    return 0;
}
