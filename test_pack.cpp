#include <iostream>
#include <cstdint>
#pragma pack(push, 1)
struct PacketSpawnItem {
    uint32_t matchId = 0;
    uint16_t opcode = 0;
    uint32_t itemId = 0;
    float x = 0.f, y = 0.f, z = 0.f;
};
#pragma pack(pop)
int main() { std::cout << sizeof(PacketSpawnItem) << std::endl; return 0; }
