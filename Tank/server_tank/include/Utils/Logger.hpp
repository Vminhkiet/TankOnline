#pragma once

#include <string>
#include <queue>
#include <mutex>
#include <thread>
#include <condition_variable>
#include <fstream>
#include <fmt/core.h>
#include <fmt/color.h>
#include <fmt/chrono.h>

enum class LogLevel {
    INFO,
    WARN,
    ERR,
    DEBUG
};

class Logger {
public:

    static Logger& getInstance() {
        static Logger instance;
        return instance;
    }

    Logger(const Logger&) = delete;
    Logger& operator=(const Logger&) = delete;

    void init(const std::string& filename);
    void stop();

    template <typename... Args>
    void log(LogLevel level, fmt::string_view format_str, Args&&... args) {
        if (!_running) return;

        std::string message = fmt::vformat(format_str, fmt::make_format_args(args...));

        LogMessage logMsg{ level, std::move(message) };

        {
            std::lock_guard<std::mutex> lock(_queueMutex);
            _logQueue.push(std::move(logMsg));
        }
        _cv.notify_one();
    }

private:
    Logger();
    ~Logger();

    struct LogMessage {
        LogLevel level;
        std::string text;
    };

    void processEntries();

    std::queue<LogMessage> _logQueue;
    std::mutex _queueMutex;
    std::condition_variable _cv;

    std::thread _workerThread;
    std::atomic<bool> _running;
    std::ofstream _fileStream;
};

#define LOG_INFO(...)  Logger::getInstance().log(LogLevel::INFO, __VA_ARGS__)
#define LOG_WARN(...)  Logger::getInstance().log(LogLevel::WARN, __VA_ARGS__)
#define LOG_ERR(...)   Logger::getInstance().log(LogLevel::ERR, __VA_ARGS__)
#define LOG_DEBUG(...) Logger::getInstance().log(LogLevel::DEBUG, __VA_ARGS__)