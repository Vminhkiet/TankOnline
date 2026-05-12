#pragma once
#include "BitWriter.h"

class WriteStream {
public:
	enum { IsWriting = 1 };
	enum { IsReading = 0 };

	WriteStream( uint32_t* buffer, int maxWords ) : m_writer( buffer, maxWords) {}
	bool SerializeInteger(int32_t value, int32_t min, int32_t max);
	void SerializePadding() { m_writer.AlignToByte(); }
	~WriteStream() {
		m_writer.Flush();
	}

private:
	BitWriter m_writer;
};