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

#define serialize_bool(stream, value) \
	do { \
		int32_t int32_value; \
		if (Stream::IsWriting) { \
			int32_value = value ? 1 : 0; \
		} \
		if( !stream.SerializeInteger(int32_value, 0, 1)) \
		{ \
			return false; \
		} \
		if( Stream::IsReading ) \
		{ \
			value = (int32_value != 0); \
		} \
	} while(0)