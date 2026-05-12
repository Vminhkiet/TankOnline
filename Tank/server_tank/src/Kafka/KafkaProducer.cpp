#include "Kafka/KafkaProducer.hpp"
#include "Utils/Logger.hpp"
#include <librdkafka/rdkafkacpp.h>

struct KafkaProducer::Impl {
    RdKafka::Producer* producer = nullptr;
};

KafkaProducer::KafkaProducer() : _impl(std::make_unique<Impl>()) {}

KafkaProducer::~KafkaProducer() {
    flush(3000);
    if (_impl->producer) {
        delete _impl->producer;
        _impl->producer = nullptr;
    }
}

bool KafkaProducer::connect(const std::string& brokers) {
    std::string err;
    auto* conf = RdKafka::Conf::create(RdKafka::Conf::CONF_GLOBAL);
    if (conf->set("bootstrap.servers", brokers, err) != RdKafka::Conf::CONF_OK) {
        LOG_ERR("KafkaProducer: config error: {}", err);
        delete conf;
        return false;
    }

    _impl->producer = RdKafka::Producer::create(conf, err);
    delete conf;

    if (!_impl->producer) {
        LOG_ERR("KafkaProducer: failed to create producer: {}", err);
        return false;
    }

    LOG_INFO("KafkaProducer: connected to {}", brokers);
    return true;
}

bool KafkaProducer::publish(const std::string& topic, const std::string& payload) {
    if (!_impl->producer) return false;

    RdKafka::ErrorCode ec = _impl->producer->produce(
        topic,
        RdKafka::Topic::PARTITION_UA,
        RdKafka::Producer::RK_MSG_COPY,
        const_cast<char*>(payload.data()), payload.size(),
        nullptr, 0, 0, nullptr, nullptr
    );

    if (ec != RdKafka::ERR_NO_ERROR) {
        LOG_ERR("KafkaProducer: produce error on {}: {}", topic, RdKafka::err2str(ec));
        return false;
    }

    _impl->producer->poll(0);  // trigger delivery callbacks
    return true;
}

void KafkaProducer::flush(int timeoutMs) {
    if (_impl->producer)
        _impl->producer->flush(timeoutMs);
}
