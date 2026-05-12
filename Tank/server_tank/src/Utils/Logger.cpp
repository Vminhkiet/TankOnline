#include "Utils/Logger.hpp"
#include <iostream>
Logger::Logger() : _running(false) {}

Logger::~Logger() {
    stop();
}

void Logger::init(const std::string& filename) {
    _fileStream.open(filename, std::ios::app);
    if (!_fileStream.is_open()) {
        std::cerr << "Khong the mo file log: " << filename << std::endl;
        return;
    }

    _running = true;
    _workerThread = std::thread(&Logger::processEntries, this);

    LOG_INFO("Hệ thống Logger đã khởi động.");
}

void Logger::stop() {
    if (_running) {
        _running = false;
        _cv.notify_all();
        if (_workerThread.joinable()) {
            _workerThread.join();
        }
        if (_fileStream.is_open()) {
            _fileStream.close();
        }
    }
}

void Logger::processEntries() {
    while (_running) {
        LogMessage msg;

        {
            std::unique_lock<std::mutex> lock(_queueMutex);
            _cv.wait(lock, [this]() { return !_logQueue.empty() || !_running; });

            if (!_running && _logQueue.empty()) break;

            msg = std::move(_logQueue.front());
            _logQueue.pop();
        }

        auto now = std::chrono::system_clock::now();
        std::time_t time_now = std::chrono::system_clock::to_time_t(now);
        std::tm tm{};
        localtime_s(&tm, &time_now);

        std::string timeStr = fmt::format("{:%Y-%m-%d %H:%M:%S}", tm);

        std::string levelStr;
        fmt::color color;

        switch (msg.level) {
        case LogLevel::INFO:  levelStr = "[INFO] "; color = fmt::color::light_green; break;
        case LogLevel::WARN:  levelStr = "[WARN] "; color = fmt::color::yellow; break;
        case LogLevel::ERR:   levelStr = "[ERR]  "; color = fmt::color::red; break;
        case LogLevel::DEBUG: levelStr = "[DEBUG]"; color = fmt::color::cyan; break;
        }

        fmt::print(fg(color), "[{}] {} {}\n", timeStr, levelStr, msg.text);

        if (_fileStream.is_open()) {
            _fileStream << "[" << timeStr << "] " << levelStr << " " << msg.text << "\n";
            _fileStream.flush();
        }
    }
}