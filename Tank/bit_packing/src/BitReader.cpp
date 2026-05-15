#include "BitReader.h"
#include <cassert>

#define WORD_SIZE_BYTES 4

uint64_t BitReader::ReadBits(int numBits) {
	if (m_error || numBits > 64 || numBits <= 0)
		return 0;
	while (m_scratchBits < numBits) {
		if (m_wordIndex * WORD_SIZE_BYTES >= m_bytesRead) {
			m_error = true;
			return 0;
		}
		m_scratch |= ((uint64_t)m_buffer[m_wordIndex] << m_scratchBits);
		m_scratchBits += 32;
		m_wordIndex++;
	}
	uint64_t result = (m_scratch & (uint64_t)((1ULL << numBits) - 1));

	m_scratch >>= numBits;
	m_scratchBits -= numBits;

	m_totalBitsRead += numBits;

	return result;
}

int BitReader::GetBitsRead() const
{
	return m_totalBitsRead;
}