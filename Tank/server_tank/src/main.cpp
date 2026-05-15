#include "Core/MatchManager.hpp"
#include "Network/NetworkManager.hpp"
#include "Utils/Logger.hpp"
#include <csignal>
#include <iostream>
#include <string>
#include <cstdlib>
#include <thread>
#include <timeapi.h>   // timeBeginPeriod / timeEndPeriod (winmm)

// HTTP management server — nhận lệnh tạo match từ Java Matchmaking Service
#define CPPHTTPLIB_OPENSSL_SUPPORT 0
#include <httplib.h>
#include <nlohmann/json.hpp>
using json = nlohmann::json;

// TODO: dùng Kafka khi production
// #include "Kafka/KafkaConsumer.hpp"

static std::string getEnv(const char* key, const char* def) {
    const char* v = std::getenv(key);
    return v ? v : def;
}

static std::atomic<bool> g_running{true};
static void onSignal(int) { g_running = false; }

// ── HTTP management server (chạy trên thread riêng) ─────────────────────────
// Endpoint: POST /internal/match/create
// Body JSON: { "matchId": 1, "playerIds": [1, 2], "mapName": "world", "maxDurationSecs": 300 }
static void startMgmtServer(MatchManager& manager, int mgmtPort) {
    auto* srv = new httplib::Server();

    srv->Post("/internal/match/create", [&manager](const httplib::Request& req,
                                                    httplib::Response&      res) {
        try {
            auto j = json::parse(req.body);

            MatchConfig cfg;
            cfg.matchId         = j.at("matchId").get<uint32_t>();
            cfg.mapName         = j.value("mapName", "world");
            cfg.maxDurationSecs = j.value("maxDurationSecs", 300);

            for (auto& pid : j.at("playerIds"))
                cfg.playerIds.push_back(pid.get<uint32_t>());

            manager.createMatch(std::move(cfg));

            LOG_INFO("mgmt: match created via HTTP (matchId={})", cfg.matchId);
            res.set_content("{\"status\":\"ok\"}", "application/json");
        } catch (const std::exception& e) {
            LOG_ERR("mgmt: bad request — {}", e.what());
            res.status = 400;
            res.set_content(
                std::string("{\"error\":\"") + e.what() + "\"}",
                "application/json"
            );
        }
    });

    // Health check — dùng bởi Monitoring Service
    srv->Get("/metrics", [](const httplib::Request&, httplib::Response& res) {
        res.set_content("{\"status\":\"online\"}", "application/json");
    });

    LOG_INFO("mgmt: HTTP management server listening on :{}", mgmtPort);
    srv->listen("0.0.0.0", mgmtPort);
    delete srv;
}

int main() {
    // Giảm độ phân giải timer Windows từ 15ms xuống 1ms → tick loop chính xác 60Hz
    timeBeginPeriod(1);

    std::cout << "=========================================\n"
              << "        SERVER-TANK  v0.3 (match mode)  \n"
              << "=========================================\n\n";

    std::signal(SIGINT,  onSignal);
    std::signal(SIGTERM, onSignal);

    Logger::getInstance().init("server.log");

    const int udpPort  = std::stoi(getEnv("UDP_PORT",  "8080"));
    const int mgmtPort = std::stoi(getEnv("MGMT_PORT", "9090"));

    // TODO: dùng Kafka khi production
    // const std::string kafkaBrokers = getEnv("KAFKA_BROKERS",  "localhost:9092");
    // const std::string kafkaGroupId = getEnv("KAFKA_GROUP_ID", "tank-server");
    // const std::string kafkaTopic   = getEnv("KAFKA_TOPIC_IN", "match.create");

    // ── Network (IOCP) ───────────────────────────────────────────────────────
    NetworkManager network;
    if (!network.start(udpPort)) {
        LOG_ERR("main: failed to bind UDP port {}", udpPort);
        return 1;
    }

    // ── Match manager ────────────────────────────────────────────────────────
    MatchManager manager(network);
    manager.start("");

    // ── HTTP management server (thread riêng, nhận lệnh từ Java) ────────────
    std::thread mgmtThread([&manager, mgmtPort]() {
        startMgmtServer(manager, mgmtPort);
    });
    mgmtThread.detach();

    // ── Test match khi dev (xóa khi production dùng HTTP/Kafka) ─────────────
    // Chỉ tạo nếu không có matchmaking service đang chạy
#ifdef TANK_DEV_HARDCODE_MATCH
    {
        MatchConfig cfg;
        cfg.matchId         = 1;
        cfg.mapName         = "world";
        cfg.maxDurationSecs = 600;
        cfg.playerIds       = {1, 2};
        manager.createMatch(std::move(cfg));
        LOG_INFO("main: DEV test match created (matchId=1, players=[1,2])");
    }
#endif

    LOG_INFO("main: UDP={} MGMT_HTTP={}", udpPort, mgmtPort);

    // TODO: Kafka consumer — bật lại khi production
    // KafkaConsumer consumer;
    // bool kafkaOk = consumer.connect(kafkaBrokers, kafkaGroupId, {kafkaTopic});
    // if (!kafkaOk) LOG_WARN("main: Kafka unavailable");
    // while (g_running) {
    //     if (!kafkaOk) { std::this_thread::sleep_for(std::chrono::milliseconds(100)); continue; }
    //     consumer.poll(500, [&](const KafkaMessage& msg) {
    //         auto j = json::parse(msg.payload);
    //         MatchConfig cfg;
    //         cfg.matchId         = j.at("matchId").get<uint32_t>();
    //         cfg.mapName         = j.value("mapName", "world");
    //         cfg.maxDurationSecs = j.value("maxDuration", 300);
    //         for (auto& pid : j.at("players"))
    //             cfg.playerIds.push_back(pid.get<uint32_t>());
    //         manager.createMatch(std::move(cfg));
    //     });
    // }
    // consumer.close();

    while (g_running)
        std::this_thread::sleep_for(std::chrono::milliseconds(100));

    LOG_INFO("main: shutting down");
    manager.stop();
    network.stop();
    timeEndPeriod(1);
    return 0;
}
