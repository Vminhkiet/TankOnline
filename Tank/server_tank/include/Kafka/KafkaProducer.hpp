#pragma once
#include <string>
#include <memory>

class KafkaProducer {
public:
    KafkaProducer();
    ~KafkaProducer();

    bool connect(const std::string& brokers);
    bool publish(const std::string& topic, const std::string& payload);
    void flush(int timeoutMs = 3000);

private:
    struct Impl;
    std::unique_ptr<Impl> _impl;
};
