#pragma once
#include <atomic>
#include <cstdint>
#include <chrono>

class MetricsCollector {
public:
    void recordTick(int64_t tickDurationUs) {
        _statSumUs.fetch_add(tickDurationUs, std::memory_order_relaxed);
        
        int64_t prevMax = _statMaxUs.load(std::memory_order_relaxed);
        while (tickDurationUs > prevMax && !_statMaxUs.compare_exchange_weak(prevMax, tickDurationUs, std::memory_order_relaxed)) {}

        int64_t prevMin = _statMinUs.load(std::memory_order_relaxed);
        while (tickDurationUs < prevMin && !_statMinUs.compare_exchange_weak(prevMin, tickDurationUs, std::memory_order_relaxed)) {}
        
        _statTicks.fetch_add(1, std::memory_order_relaxed);
    }

    void recordBudgetViolation() {
        _statOverruns.fetch_add(1, std::memory_order_relaxed);
    }

    void reset() {
        _statTicks = 0;
        _statSumUs = 0;
        _statMaxUs = 0;
        _statMinUs = INT64_MAX;
        _statOverruns = 0;
    }

    int getTicks() const { return _statTicks.load(); }
    int64_t getSumUs() const { return _statSumUs.load(); }
    int64_t getMaxUs() const { return _statMaxUs.load(); }
    int64_t getMinUs() const { return _statMinUs.load(); }
    int getOverruns() const { return _statOverruns.load(); }

private:
    std::atomic<int> _statTicks{0};
    std::atomic<int64_t> _statSumUs{0};
    std::atomic<int64_t> _statMaxUs{0};
    std::atomic<int64_t> _statMinUs{INT64_MAX};
    std::atomic<int> _statOverruns{0};
};
