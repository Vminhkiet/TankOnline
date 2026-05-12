#pragma once
#include <cstdint>
#include <assert.h>

inline int bits_required(uint32_t min, uint32_t max) {
	if (min == max) return 0;
	uint32_t range = max - min;

	unsigned int bits = 0;
	while ((1ULL << bits) <= range) {
		bits++;
	}
	return bits;
}