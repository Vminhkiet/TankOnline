#include "Memory/BufferPool.hpp"

BufferPool::BufferPool(size_t size) : _capacity(size) {
	for (size_t i = 0; i < size; i++) {
		IoContext* io = new IoContext();
		_pool.push(io);
	}
}

BufferPool::~BufferPool() {
	std::lock_guard<std::mutex> lock(_mtx);
	while (!_pool.empty())
	{
		delete _pool.front();
		_pool.pop();
	}
	std::cout << "[BUFFER_POOL] Da giai phong toan bo bo nho.\n";
}

IoContext* BufferPool::acquire(IoOperation op) {
	std::lock_guard<std::mutex> lock(_mtx);
	if (_pool.empty()) {
		std::cerr << "[BUFFER_POOL] POOL EMPTY.";
		IoContext* emergencyContext = new IoContext();
		emergencyContext->reset(op);
		return emergencyContext;
	}

	IoContext* p = _pool.front();
	_pool.pop();
	p->reset(op);

	return p;
}

void BufferPool::release(IoContext* io)
{
	if (io) {
		std::lock_guard<std::mutex> lock(_mtx);
		_pool.push(io);
	}
}

size_t BufferPool::getAvailableCount() {
	std::lock_guard<std::mutex> lock(_mtx);
	return _pool.size();
}