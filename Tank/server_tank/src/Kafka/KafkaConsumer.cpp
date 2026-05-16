#include "Kafka/KafkaConsumer.hpp"
#include "Utils/Logger.hpp"
#include <librdkafka/rdkafkacpp.h>

struct KafkaConsumer::Impl {
    RdKafka::KafkaConsumer* consumer = nullptr;
};

KafkaConsumer::KafkaConsumer() : _impl(std::make_unique<Impl>()) {}

KafkaConsumer::~KafkaConsumer() { close(); }

bool KafkaConsumer::connect(const std::string& brokers,
                            const std::string& groupId,
                            const std::vector<std::string>& topics) {
    std::string err;
    auto* conf = RdKafka::Conf::create(RdKafka::Conf::CONF_GLOBAL);

    if (conf->set("bootstrap.servers", brokers, err) != RdKafka::Conf::CONF_OK ||
        conf->set("group.id",          groupId, err) != RdKafka::Conf::CONF_OK ||
        conf->set("auto.offset.reset", "latest", err) != RdKafka::Conf::CONF_OK) {
        LOG_ERR("KafkaConsumer: config error: {}", err);
        delete conf;
        return false;
    }

    _impl->consumer = RdKafka::KafkaConsumer::create(conf, err);
    delete conf;

    if (!_impl->consumer) {
        LOG_ERR("KafkaConsumer: failed to create consumer: {}", err);
        return false;
    }

    RdKafka::ErrorCode ec = _impl->consumer->subscribe(topics);
    if (ec != RdKafka::ERR_NO_ERROR) {
        LOG_ERR("KafkaConsumer: subscribe error: {}", RdKafka::err2str(ec));
        return false;
    }

    LOG_INFO("KafkaConsumer: connected to {} | topics: {}", brokers, topics[0]);
    return true;
}

bool KafkaConsumer::poll(int timeoutMs,
                         const std::function<void(const KafkaMessage&)>& cb) {
    if (!_impl->consumer) return false;

    auto* msg = _impl->consumer->consume(timeoutMs);
    if (!msg) return true;

    if (msg->err() == RdKafka::ERR__TIMED_OUT) {
        delete msg;
        return true;
    }
    if (msg->err() != RdKafka::ERR_NO_ERROR) {
        auto errCode = msg->err();
        LOG_ERR("KafkaConsumer: error: {}", msg->errstr());
        delete msg;
        return errCode != RdKafka::ERR__ALL_BROKERS_DOWN;
    }

    KafkaMessage km;
    km.topic   = msg->topic_name();
    km.payload = std::string(static_cast<const char*>(msg->payload()), msg->len());
    km.offset  = msg->offset();
    cb(km);

    delete msg;
    return true;
}

void KafkaConsumer::close() {
    if (_impl && _impl->consumer) {
        _impl->consumer->close();
        delete _impl->consumer;
        _impl->consumer = nullptr;
    }
}
