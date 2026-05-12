#pragma once
#include "IoContext.hpp"
#include <queue>
#include <mutex>
#include <iostream>

class BufferPool {
private:
	std::queue<IoContext*> _pool;
	size_t _capacity;
	std::mutex _mtx;
public:
	BufferPool(size_t size);
	~BufferPool();
	IoContext* acquire(IoOperation op);
	void release(IoContext* p);
	size_t getAvailableCount();
};