#include "Core/MatchManager.hpp"
#include "Network/NetworkManager.hpp"
#include "Utils/Logger.hpp"
#include <csignal>
#include <iostream>
#include <string>
#include <cstdlib>
#include <timeapi.h>   // timeBeginPeriod / timeEndPeriod (winmm)

// TODO: dùng Kafka khi production
// #include "Kafka/KafkaConsumer.hpp"
// #include <nlohmann/json.hpp>
// using json = nlohmann::json;

static std::string getEnv(const char* key, const char* def) {
    const char* v = std::getenv(key);
    return v ? v : def;
}

static std::atomic<bool> g_running{true};
static void onSignal(int) { g_running = false; }

int main() {
    // Reduce Windows multimedia timer resolution from default 15 ms to 1 ms.
    // This makes sleep_for accurate to ~1 ms instead of ~15 ms, preventing the
    // tick loop from running at ~64 Hz instead of 60 Hz.
    timeBeginPeriod(1);

    std::cout << "=========================================\n"
              << "        SERVER-TANK  v0.2 (match mode)  \n"
              << "=========================================\n\n";

    std::signal(SIGINT,  onSignal);
    std::signal(SIGTERM, onSignal);

    Logger::getInstance().init("server.log");

    const int udpPort = std::stoi(getEnv("UDP_PORT", "8080"));

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

    // ── Test match (hardcode, xoá khi production dùng Kafka) ────────────────
    {
        MatchConfig cfg;
        cfg.matchId         = 1;
        cfg.mapName         = "world";
        cfg.maxDurationSecs = 600;
        cfg.playerIds       = {1, 2};
        manager.createMatch(std::move(cfg));
        LOG_INFO("main: test match created (matchId=1, players=[1,2])");
    }

    // TODO: Kafka consumer event loop — bật lại khi production
    // KafkaConsumer consumer;
    // bool kafkaOk = consumer.connect(kafkaBrokers, kafkaGroupId, {kafkaTopic});
    // if (!kafkaOk)
    //     LOG_WARN("main: Kafka unavailable");
    //
    // while (g_running) {
    //     if (!kafkaOk) { std::this_thread::sleep_for(std::chrono::milliseconds(100)); continue; }
    //     bool ok = consumer.poll(500, [&](const KafkaMessage& msg) {
    //         try {
    //             auto j = json::parse(msg.payload);
    //             MatchConfig cfg;
    //             cfg.matchId         = j.at("matchId").get<uint32_t>();
    //             cfg.mapName         = j.value("mapName", "world");
    //             cfg.maxDurationSecs = j.value("maxDuration", 300);
    //             for (auto& pid : j.at("players"))
    //                 cfg.playerIds.push_back(pid.get<uint32_t>());
    //             manager.createMatch(std::move(cfg));
    //         } catch (const std::exception& e) {
    //             LOG_ERR("main: bad match.create payload: {}", e.what());
    //         }
    //     });
    //     if (!ok) { LOG_ERR("main: Kafka fatal error"); break; }
    // }
    // consumer.close();

    LOG_INFO("main: running (UDP={})", udpPort);

    while (g_running)
        std::this_thread::sleep_for(std::chrono::milliseconds(100));

    LOG_INFO("main: shutting down");
    manager.stop();
    network.stop();
    timeEndPeriod(1);
    return 0;
}
