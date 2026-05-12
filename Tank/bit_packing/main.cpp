#include "ReadStream.h"
#include "WriteStream.h"
#include "PlayerState.h"

#include <iostream>

int main() {
	std::cout << "--- Bat dau kiem tra He thong Serialization ---" << std::endl;
	const int MAX_WORDS = 256;
	uint32_t buffer[MAX_WORDS];

	PlayerState player_write;
	player_write.player_id = 42;
	player_write.health = 100;

	std::cout << "Bat dau phien GHI..." << std::endl;
	{
		
		std::cout << "  Dang ghi -> ID: " << player_write.player_id
			<< ", Health: " << player_write.health << std::endl;
		WriteStream writeStream(buffer, MAX_WORDS);

		if (!player_write.Serialize(writeStream)) {
			std::cout << "LOI: GHI that bai!" << std::endl;
			return 1;
		}
		writeStream.SerializePadding();
	}

	std::cout << "...GHI thanh cong." << std::endl;
	std::cout << std::endl;

	std::cout << "Bat dau phien DOC..." << std::endl;
	PlayerState player_read;

	player_read.player_id = 0;
	player_read.health = 0;

	{
		ReadStream readStream(buffer, MAX_WORDS * 4);
		if (!player_read.Serialize(readStream))
		{
			std::cout << "LOI: DOC that bai!" << std::endl;
			return 1;
		}
	}

	std::cout << "...DOC thanh cong." << std::endl;
	std::cout << "  Doc duoc -> ID: " << player_read.player_id
		<< ", Health: " << player_read.health << std::endl;
	std::cout << std::endl;
	std::cout << "--- KET QUA KIEM TRA ---" << std::endl;

	if (player_write.player_id == player_read.player_id &&
		player_write.health == player_read.health)
	{
		std::cout << "[SUCCESS] Du lieu khop hoan hao!" << std::endl;
	}
	else
	{
		std::cout << "[FAILURE] Du lieu KHONG khop!" << std::endl;
	}

	return 0;
}