#pragma once

#include "SerializationUtils.h"

#define serialize_int(stream, value, min, max)	\
	do {	\
		assert(min < max);	\
		int32_t int32_value;	\
		if (Stream::IsWriting) {	\
			assert(value >= min);	\
			assert(value <= max);	\
									\
			int32_value = (int32_t)value;	\
		}								\
		if( !stream.SerializeInteger(int32_value, min, max))	\
		{														\
			return false;										\
		}														\
																\
		if( Stream::IsReading )									\
		{														\
			value = int32_value;								\
			if (value < min || value > max)						\
			{													\
				return false;									\
			}													\
		}														\
	} while(0)