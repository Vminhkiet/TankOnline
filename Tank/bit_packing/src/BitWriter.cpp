#include "BitWriter.h"
#include <cstring>
#include <cassert>


void BitWriter::WriteBits(uint64_t value, int numBits) {
	value &= (1ULL << numBits) - 1;

	m_scratch |= (value << m_scratchBits);

	m_scratchBits += numBits;

	while (m_scratchBits >= 32) {
		if (m_wordIndex >= m_maxWords)
			return;
		m_buffer[m_wordIndex] = (uint32_t)m_scratch;
		m_scratch >>= 32;
		m_scratchBits -= 32;
		m_wordIndex++;
	}
}

void BitWriter::Flush() {
	if (m_scratchBits > 0)
	{
		assert(m_wordIndex < m_maxWords);
		m_buffer[m_wordIndex] = (uint32_t)(m_scratch & 0xFFFFFFFF);
		m_scratchBits = 0;
		m_scratch = 0;
		m_wordIndex++;
	}
}

void BitWriter::AlignToByte()
{
	int padding = (8 - (m_scratchBits % 8)) % 8;

	if (padding > 0)
		WriteBits(0, padding);
}