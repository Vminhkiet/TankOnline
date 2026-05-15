#pragma once
#include "BitReader.h"

class ReadStream {
public:
	enum { IsWriting = 0 };
	enum { IsReading = 1 };

	ReadStream(const uint32_t* buffer, int bufferSizeBytes) : m_reader(buffer, bufferSizeBytes) {}
	bool SerializeInteger(int32_t& value, int32_t min, int32_t max);
	int GetBytesRead() const;
private:
	BitReader m_reader;
};