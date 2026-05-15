// Stub — Kafka consumer disabled for local build. Real impl in KafkaConsumer.cpp (requires rdkafka).
#include "Kafka/KafkaConsumer.hpp"

struct KafkaConsumer::Impl {};

KafkaConsumer::KafkaConsumer()  : _impl(std::make_unique<Impl>()) {}
KafkaConsumer::~KafkaConsumer() = default;

bool KafkaConsumer::connect(const std::string&,
                            const std::string&,
                            const std::vector<std::string>&) { return false; }

bool KafkaConsumer::poll(int, const std::function<void(const KafkaMessage&)>&) { return false; }

void KafkaConsumer::close() {}
