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
    uint8_t barrelCount = 1;      // number of barrels
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
    float   turretYaw   = 0.f;
    uint8_t barrelCount = 1;

    template<typename Stream>
    bool Serialize(Stream& stream) {
        serialize_int(stream, launchForce, NetConst::FORCE_MIN, NetConst::FORCE_MAX);
        
        int32_t yawDegInt = static_cast<int32_t>(turretYaw * 180.f / 3.14159265f);
        serialize_int(stream, yawDegInt, -180, 180);
        if constexpr (Stream::IsReading) {
            turretYaw = static_cast<float>(yawDegInt) * 3.14159265f / 180.f;
        }

        serialize_int(stream, barrelCount, 1, 10);
        return true;
    }

    ClientInput toClientInput() const {
        ClientInput ci{};
        ci.shoot       = true;
        ci.launchForce = static_cast<float>(launchForce);
        ci.turretYaw   = turretYaw;
        ci.barrelCount = barrelCount;
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
    uint16_t score  = 0;
    uint8_t  placement = 0;
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
    int16_t  rpReward     = 0;
    uint8_t  placement    = 0;
    uint8_t  playerCount  = 0; // followed by MatchEndPlayer array
};

struct MatchEndPlayer {
    uint32_t tankId       = 0;
    int16_t  rpReward     = 0;
    uint16_t kills        = 0;
    char     userId[37]   = {0}; // 36-char UUID + null terminator
};

// Raw S2C force-logout header + variable UTF-8 message bytes
// Packet layout:
// [ForceLogoutPacket header][message bytes]
// total packet size = sizeof(ForceLogoutPacket) + messageLen
struct ForceLogoutPacket {
    uint32_t matchId      = 0;
    uint16_t opcode       = 0;   // Opcode::S2C_FORCE_LOGOUT
    uint16_t code         = 1003;
    uint16_t messageLen   = 0;   // bytes of UTF-8 message appended after header
    uint32_t disconnectAfterMs = 10000; // client should show reason before forced disconnect
};

// S2C_EVENT_SHOOT = 2006
struct EventShootPacket {
    uint32_t matchId      = 0;
    uint16_t opcode       = 0;   // Opcode::S2C_EVENT_SHOOT
    uint32_t shooterId    = 0;
    uint8_t  weaponType   = 0;   // 0 = Projectile, 1 = Hitscan
    uint8_t  barrelCount  = 1;
    float    turretYaw    = 0.f;
    uint32_t hitTankId    = 0;   // 0 if no hit or projectile
    float    hitX = 0.f, hitY = 0.f, hitZ = 0.f;
};

#pragma pack(pop)
