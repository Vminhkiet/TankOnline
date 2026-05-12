#pragma once
#include <string>
#include <vector>
#include <functional>
#include <memory>

struct KafkaMessage {
    std::string topic;
    std::string payload;
    int64_t     offset = 0;
};

class KafkaConsumer {
public:
    KafkaConsumer();
    ~KafkaConsumer();

    bool connect(const std::string& brokers,
                 const std::string& groupId,
                 const std::vector<std::string>& topics);

    // Blocks up to timeoutMs. Calls cb for each message. Returns false on fatal error.
    bool poll(int timeoutMs, const std::function<void(const KafkaMessage&)>& cb);

    void close();

private:
    struct Impl;
    std::unique_ptr<Impl> _impl;
};
