#pragma once

#include "SerializationUtils.h"
#include <cstring>

class BitWriter {
public:
    BitWriter(uint32_t* buffer, int maxWords)
        : m_buffer(buffer),
        m_maxWords(maxWords)
    {
        m_wordIndex = 0;
        m_scratch = 0;
        m_scratchBits = 0;

        memset(m_buffer, 0, (size_t)maxWords * 4);
    }

	void WriteBits(uint64_t value, int numBits);
    void AlignToByte();
	void Flush();
private:
	uint32_t* m_buffer;
	int m_wordIndex;
	uint64_t m_scratch;
	int m_scratchBits;
	int m_maxWords;
};