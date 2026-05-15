#pragma once
#include "Serialization.h"

struct PlayerState
{
	int player_id;
	int health;
	template<typename Stream>
	bool Serialize(Stream& stream)
	{
		serialize_int(stream, player_id, 0, 255);
		serialize_int(stream, health, 0, 100);
		
		return true;
	}
};