#pragma once
#include <cstdint>

enum class Opcode : uint16_t {
    C2S_LOGIN        = 1000,
    C2S_MOVE         = 1001,
    C2S_SHOOT        = 1002,
    C2S_PING         = 1003,
    C2S_CAST_SKILL   = 1004,
    C2S_START_CHARGE_SKILL = 1005,

    S2C_SNAPSHOT     = 2000,  // raw binary: SnapshotHeader + TankState[] + BulletState[]
    S2C_STATE_SYNC   = 2001,
    S2C_EVENT_SPAWN  = 2002,
    S2C_EVENT_HIT    = 2003,
    S2C_MATCH_END    = 2004,  // raw binary: MatchEndPacket (14 bytes, per-player myKills)
    S2C_FORCE_LOGOUT = 2005,  // raw binary: ForceLogoutPacket + UTF-8 message bytes
    S2C_EVENT_SHOOT  = 2006,  // raw binary: EventShootPacket
    S2C_PONG         = 2007,  // raw binary: PongPacket
    S2C_EVENT_SPAWN_ITEM = 2008, // raw binary: PacketSpawnItem
    S2C_EVENT_DESPAWN_ITEM = 2009, // raw binary: PacketDespawnItem
    S2C_EVENT_SKILL_CAST = 2010, // raw binary: EventSkillCastPacket
    S2C_EVENT_START_CHARGE_SKILL = 2011, // raw binary: EventStartChargeSkillPacket
};