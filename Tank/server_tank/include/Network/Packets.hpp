#pragma once
#include <cstdint>
#include <vector>
#include "Network/Opcode.hpp"
#include "Serialization.h"
#include "Network/NetworkConstants.h"
#include "Physics/PhysicsTypes.hpp"

#pragma pack(push, 1)

// ─── Packet header (bi-directional) ─────────────────────────────────────────

struct PacketHeader {
    uint16_t size    = 0;
    Opcode   opcode  = Opcode::C2S_LOGIN;
    uint32_t matchId = 0;   // which match this packet belongs to
    uint8_t  flags   = 0;
    uint8_t  seq     = 0;
    uint16_t tick    = 0;

    template<typename Stream>
    bool Serialize(Stream& stream) {
        serialize_int(stream, size,    NetConst::PACKET_SIZE_MIN, NetConst::PACKET_SIZE_MAX);
        uint16_t op = static_cast<uint16_t>(opcode);
        serialize_int(stream, op,      NetConst::UINT16_MIN,      NetConst::UINT16_Max);
        if (Stream::IsReading) opcode = static_cast<Opcode>(op);
        serialize_int(stream, matchId, NetConst::MATCH_ID_MIN,    NetConst::MATCH_ID_MAX);
        serialize_int(stream, flags,   NetConst::FLAGS_MIN,        NetConst::FLAGS_MAX);
        serialize_int(stream, seq,     NetConst::SEQ_MIN,          NetConst::SEQ_MAX);
        serialize_int(stream, tick,    NetConst::TICK_MIN,         NetConst::TICK_MAX);
        return true;
    }
};

// ─── C2S: movement + shoot input ────────────────────────────────────────────

struct ClientInput {
    int8_t  moveX       = 0;      // -1 left, 0 none, +1 right
    int8_t  moveZ       = 0;      // -1 back, 0 none, +1 forward
    float   turretYaw   = 0.f;
    bool    shoot       = false;
    uint8_t seq         = 0;
    float   launchForce = 20.f;   // bullet speed (m/s) when shoot=true
};

struct PacketMovement {
    uint8_t dirX  = 1;   // 0=left, 1=none, 2=right
    uint8_t dirZ  = 1;   // 0=back, 1=none, 2=forward
    uint8_t speed = 0;

    template<typename Stream>
    bool Serialize(Stream& stream) {
        serialize_int(stream, dirX,  NetConst::DIR_X_MIN, NetConst::DIR_X_MAX);
        serialize_int(stream, dirZ,  NetConst::DIR_Y_MIN, NetConst::DIR_Y_MAX);
        serialize_int(stream, speed, NetConst::SPEED_MIN, NetConst::SPEED_MAX);
        return true;
    }

    ClientInput toClientInput(uint8_t s = 0) const {
        ClientInput ci;
        ci.moveX = static_cast<int8_t>(dirX) - 1;
        ci.moveZ = static_cast<int8_t>(dirZ) - 1;
        ci.seq   = s;
        return ci;
    }
};

struct PacketShoot {
    uint8_t launchForce = 20;  // integer m/s in [FORCE_MIN, FORCE_MAX]

    template<typename Stream>
    bool Serialize(Stream& stream) {
        serialize_int(stream, launchForce, NetConst::FORCE_MIN, NetConst::FORCE_MAX);
        return true;
    }

    ClientInput toClientInput() const {
        ClientInput ci{};
        ci.shoot       = true;
        ci.launchForce = static_cast<float>(launchForce);
        return ci;
    }
};

// ─── S2C: per-tank state entry ───────────────────────────────────────────────

struct TankState {
    uint32_t tankId = 0;
    float    x = 0.f, y = 0.f, z = 0.f;
    float    yaw    = 0.f;
    int16_t  health = 100;
    uint8_t  flags  = 0;   // bit0 = isAlive
};

// ─── S2C: per-bullet state entry ─────────────────────────────────────────────

struct BulletState {
    uint32_t bulletId = 0;
    uint32_t ownerId  = 0;   // tankId that fired this bullet
    float    x = 0.f, y = 0.f, z = 0.f;
};

// ─── S2C: match-end notification (14 bytes, sent individually per player) ────
// outcome: 0=win 1=lose 2=draw 3=timeout  (from recipient's POV)
// winnerId: valid when outcome==win; 0 otherwise
// myKills: kill count for the specific recipient of this packet

struct MatchEndPacket {
    uint32_t matchId      = 0;
    uint16_t opcode       = 0;   // Opcode::S2C_MATCH_END
    uint8_t  outcome      = 0;
    uint32_t winnerId     = 0;
    uint16_t durationSecs = 0;
    uint16_t myKills      = 0;
};

#pragma pack(pop)
