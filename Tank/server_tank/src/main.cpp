#include "Core/MatchManager.hpp"
#include "Network/INetworkBackend.hpp"
#include "Network/NetworkManager.hpp"
#include "Network/BlockingBackend.hpp"
#include "Utils/Logger.hpp"
#include <csignal>
#include <iostream>
#include <string>
#include <cstdlib>
#include <timeapi.h>   // timeBeginPeriod / timeEndPeriod (winmm)

#include "Kafka/KafkaConsumer.hpp"
#include <nlohmann/json.hpp>
using json = nlohmann::json;

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

    const std::string logFile = getEnv("LOG_FILE", "server.log");
    Logger::getInstance().init(logFile);

    const int udpPort = std::stoi(getEnv("UDP_PORT", "8080"));

    // Kafka broker list — set KAFKA_BROKERS=host:9092 to enable publishing.
    // Leave empty (default) to run without Kafka (stub silently no-ops).
    const std::string kafkaBrokers = getEnv("KAFKA_BROKERS", "172.25.203.168:9092");
    const std::string kafkaGroupId = getEnv("KAFKA_GROUP_ID", "tank-server");
    const std::string kafkaTopic   = getEnv("KAFKA_TOPIC_IN",  "match.create");

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

    // ── Match manager ────────────────────────────────────────────────────────
    MatchManager manager(*netPtr);
    manager.start(kafkaBrokers);

    // ── Create N test matches (NUM_MATCHES env var, default 1) ──────────────
    // Each match gets 2 player slots: match M → playerIds {2M-1, 2M}
    const int numMatches = std::stoi(getEnv("NUM_MATCHES", "1"));
    for (int m = 1; m <= numMatches; ++m) {
        MatchConfig cfg;
        cfg.matchId         = static_cast<uint32_t>(m);
        cfg.mapName         = "world";
        cfg.maxDurationSecs = 600;
        cfg.playerIds       = { static_cast<uint32_t>(2*m - 1),
                                 static_cast<uint32_t>(2*m) };
        manager.createMatch(std::move(cfg));
    }
    LOG_INFO("main: {} test match(es) created", numMatches);

    KafkaConsumer consumer;
    bool kafkaOk = !kafkaBrokers.empty() &&
                   consumer.connect(kafkaBrokers, kafkaGroupId, {kafkaTopic});
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
                manager.createMatch(std::move(cfg));
            } catch (const std::exception& e) {
                LOG_ERR("main: bad match.create payload: {}", e.what());
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
