#include "PacketBuilder.hpp"
#include "Network/Packets.hpp"
#include "WriteStream.h"
#include <cstring>

// ─── helpers ──────────────────────────────────────────────────────────────────

// Bit-layout (contiguous across uint32 words):
//   PacketHeader  : 11+16+8+8+16 = 59 bits  →  2 words (64 bits, 5 padding)
//   PacketMovement: 8+8+8        = 24 bits  →  1 word  (32 bits, 8 padding)
// LOGIN  total: 59 bits padded to 2 words = 8 bytes
// MOVE   total: 59+24 = 83 bits padded to 3 words = 12 bytes

static constexpr int WORDS_BUF = 8; // 32 bytes – safe for all packets

// Flush the WriteStream by going out of scope, then copy exactly `byteCount`
// bytes from the uint32 word buffer into `dst`.
static int finalise(uint8_t* dst, int dstSize, const uint32_t* words, int byteCount)
{
    if (dstSize < byteCount) return -1;
    memcpy(dst, words, byteCount);
    return byteCount;
}

// ─── LOGIN ─────────────────────────────────────────────────────────────────────

int PacketBuilder::buildLogin(uint8_t* buf, int bufSize, uint8_t seq)
{
    constexpr int BYTES = 8; // 2 words
    if (bufSize < BYTES) return -1;

    uint32_t words[WORDS_BUF]{};
    {
        WriteStream ws(words, WORDS_BUF);
        PacketHeader h;
        h.size   = BYTES;
        h.opcode = Opcode::C2S_LOGIN;
        h.flags  = 0;
        h.seq    = seq;
        h.tick   = 0;
        h.Serialize(ws);
    } // ~WriteStream → Flush()
    return finalise(buf, bufSize, words, BYTES);
}

// ─── MOVE ──────────────────────────────────────────────────────────────────────

int PacketBuilder::buildMove(uint8_t* buf, int bufSize,
                              uint8_t seq, uint16_t tick,
                              int8_t moveX, int8_t moveZ)
{
    constexpr int BYTES = 12; // 3 words
    if (bufSize < BYTES) return -1;

    uint32_t words[WORDS_BUF]{};
    {
        WriteStream ws(words, WORDS_BUF);

        PacketHeader h;
        h.size   = BYTES;
        h.opcode = Opcode::C2S_MOVE;
        h.flags  = 0;
        h.seq    = seq;
        h.tick   = tick;
        h.Serialize(ws);

        PacketMovement mv;
        mv.dirX  = static_cast<uint8_t>(moveX + 1); // -1→0, 0→1, +1→2
        mv.dirZ  = static_cast<uint8_t>(moveZ + 1);
        mv.speed = 128;
        mv.Serialize(ws);
    }
    return finalise(buf, bufSize, words, BYTES);
}

// ─── SHOOT ─────────────────────────────────────────────────────────────────────

int PacketBuilder::buildShoot(uint8_t* buf, int bufSize, uint8_t seq, uint16_t tick)
{
    constexpr int BYTES = 8;
    if (bufSize < BYTES) return -1;

    uint32_t words[WORDS_BUF]{};
    {
        WriteStream ws(words, WORDS_BUF);
        PacketHeader h;
        h.size   = BYTES;
        h.opcode = Opcode::C2S_SHOOT;
        h.flags  = 0;
        h.seq    = seq;
        h.tick   = tick;
        h.Serialize(ws);
    }
    return finalise(buf, bufSize, words, BYTES);
}
