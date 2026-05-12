#pragma once
#include <winsock2.h>
#include <vector>
#include <cstdint>
#include "Network/Opcode.hpp"

struct GameCommand {
    sockaddr_in          sender;
    uint32_t             matchId   = 0;
    Opcode               op        = {};
    std::vector<uint8_t> rawBuffer;  // full packet bytes (copied before IoContext recycled)
    float                dt        = 0.f;
};
