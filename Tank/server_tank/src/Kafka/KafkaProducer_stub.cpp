// Stub — Kafka disabled for local build. Real impl in KafkaProducer.cpp (requires rdkafka).
#include "Kafka/KafkaProducer.hpp"

struct KafkaProducer::Impl {};

KafkaProducer::KafkaProducer()  : _impl(std::make_unique<Impl>()) {}
KafkaProducer::~KafkaProducer() = default;

bool KafkaProducer::connect(const std::string&)                      { return false; }
bool KafkaProducer::publish(const std::string&, const std::string&)  { return false; }
void KafkaProducer::flush(int)                                        {}
