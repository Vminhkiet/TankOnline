#include "ReadStream.h"

bool ReadStream::SerializeInteger(int32_t& value, int32_t min, int32_t max) {
	assert(min < max);
	
	const int bits = bits_required(min, max);

	uint32_t unsigned_value = (uint32_t)m_reader.ReadBits(bits);

	if (!m_reader.IsOk()) {
		return false;
	}

	value = unsigned_value + min;
	return true;
}

int ReadStream::GetBytesRead() const
{
	return (m_reader.GetBitsRead() + 7) / 8;
}