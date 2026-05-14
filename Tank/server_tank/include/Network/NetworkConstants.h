#pragma once
#include <cstdint>

namespace NetConst
{
    // Header
    constexpr uint16_t UINT16_MIN       = 0;
    constexpr uint16_t UINT16_Max       = 65535;

    constexpr uint16_t PACKET_SIZE_MIN  = 8;
    constexpr uint16_t PACKET_SIZE_MAX  = 1400;

    constexpr uint8_t  FLAGS_MIN        = 0;
    constexpr uint8_t  FLAGS_MAX        = 255;

    constexpr uint8_t  SEQ_MIN          = 0;
    constexpr uint8_t  SEQ_MAX          = 255;

    constexpr uint16_t TICK_MIN         = 0;
    constexpr uint16_t TICK_MAX         = 65535;

    constexpr uint32_t MATCH_ID_MIN     = 0;
    constexpr uint32_t MATCH_ID_MAX     = 1'000'000;

    // Packet Movement (direction: 0=left/back, 1=none, 2=right/forward)
    constexpr uint8_t  DIR_X_MIN        = 0;
    constexpr uint8_t  DIR_X_MAX        = 2;

    constexpr uint8_t  DIR_Y_MIN        = 0;
    constexpr uint8_t  DIR_Y_MAX        = 2;

    constexpr uint8_t  SPEED_MIN        = 0;
    constexpr uint8_t  SPEED_MAX        = 255;

    // Packet Shoot (launch force, maps directly to bullet speed in m/s)
    constexpr uint8_t  FORCE_MIN        = 15;
    constexpr uint8_t  FORCE_MAX        = 30;

}
