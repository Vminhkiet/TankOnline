#include "WriteStream.h"

bool WriteStream::SerializeInteger(int32_t value, int32_t min, int32_t max) {
	assert(min < max);
	assert(value >= min);
	assert(value <= max);

	unsigned int bits = bits_required(min, max);
	uint32_t unsigned_value = value - min;

	m_writer.WriteBits(unsigned_value, bits);

	return true;
}