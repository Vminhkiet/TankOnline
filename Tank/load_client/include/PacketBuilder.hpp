#pragma once
#include <cstdint>

// Builds bit-packed UDP packets matching the server's ReadStream format.
// All build* functions return the number of bytes written into buf,
// or -1 if buf is too small.
struct PacketBuilder {
    // Minimum buffer size needed for any packet
    static constexpr int BUF_SIZE = 32;

    // C2S_LOGIN  – header only, no payload
    static int buildLogin(uint8_t* buf, int bufSize, uint8_t seq);

    // C2S_MOVE   – header + PacketMovement
    //   moveX: -1 / 0 / +1  (steering)
    //   moveZ: -1 / 0 / +1  (throttle)
    static int buildMove(uint8_t* buf, int bufSize,
                         uint8_t seq, uint16_t tick,
                         int8_t moveX, int8_t moveZ);

    // C2S_SHOOT  – header only (server reads opcode, no extra payload)
    static int buildShoot(uint8_t* buf, int bufSize, uint8_t seq, uint16_t tick);
};
