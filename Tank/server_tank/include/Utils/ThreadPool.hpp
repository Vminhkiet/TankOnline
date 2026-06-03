#pragma once
#include <vector>
#include <queue>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <future>
#include <functional>
#include <atomic>
#include <stdexcept>
#ifdef _WIN32
#include <windows.h>
#endif

class ThreadPool {
public:
    // coreAffinityMask = 0 → không pin (OS scheduler tự quyết)
    // coreAffinityMask = 0x3C → pin vào core 2,3,4,5 (bit 2–5)
    explicit ThreadPool(size_t n = std::thread::hardware_concurrency(),
                        DWORD_PTR coreAffinityMask = 0) {
        for (size_t i = 0; i < n; ++i) {
            _workers.emplace_back([this] { workerLoop(); });
            if (coreAffinityMask != 0) {
#ifdef _WIN32
                SetThreadAffinityMask(
                    _workers.back().native_handle(), coreAffinityMask);
                SetThreadPriority(
                    _workers.back().native_handle(), THREAD_PRIORITY_TIME_CRITICAL);
#endif
            }
        }
    }

    ~ThreadPool() {
        {
            std::lock_guard lock(_mutex);
            _stop = true;
        }
        _cv.notify_all();
        for (auto& t : _workers) t.join();
    }

    template<class F>
    auto submit(F&& f) -> std::future<std::invoke_result_t<F>> {
        using R = std::invoke_result_t<F>;
        auto task = std::make_shared<std::packaged_task<R()>>(std::forward<F>(f));
        std::future<R> fut = task->get_future();
        {
            std::lock_guard lock(_mutex);
            if (_stop) throw std::runtime_error("ThreadPool is stopped");
            _tasks.push([task] { (*task)(); });
            _pending.fetch_add(1, std::memory_order_relaxed);
        }
        _cv.notify_one();
        return fut;
    }

    size_t size()         const { return _workers.size(); }
    size_t pendingCount() const { return _pending.load(std::memory_order_relaxed); }

private:
    void workerLoop() {
        while (true) {
            std::function<void()> task;
            {
                std::unique_lock lock(_mutex);
                _cv.wait(lock, [this] { return _stop || !_tasks.empty(); });
                if (_stop && _tasks.empty()) return;
                task = std::move(_tasks.front());
                _tasks.pop();
                _pending.fetch_sub(1, std::memory_order_relaxed);
            }
            task();
        }
    }

    std::vector<std::thread>          _workers;
    std::queue<std::function<void()>> _tasks;
    std::mutex                        _mutex;
    std::condition_variable           _cv;
    std::atomic<size_t>               _pending{0};
    bool                              _stop = false;
};
