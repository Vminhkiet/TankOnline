#pragma once
#include "SerializationUtils.h"

class BitReader {
public:
	BitReader(const uint32_t* buffer, int bytesRead)
		: m_buffer(const_cast<uint32_t*>(buffer)),
		m_bytesRead(bytesRead)
	{
		m_scratch = 0;
		m_scratchBits = 0;
		m_wordIndex = 0;
		m_error = false;
	}
	uint64_t ReadBits(int numBits);
	int GetBitsRead() const;
	bool IsOk() const { return !m_error; }
private:
	uint32_t* m_buffer;
	uint64_t m_scratch;
	int m_scratchBits;
	int m_bytesRead;
	int m_wordIndex;
	int m_totalBitsRead = 0;
	bool m_error;
};