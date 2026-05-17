// TankProtocol.cs — opcodes, constants, packet structs matching server exactly
using System.Runtime.InteropServices;

namespace TankNet
{
    public enum Opcode : ushort
    {
        C2S_LOGIN    = 1000,
        C2S_MOVE     = 1001,
        C2S_SHOOT       = 1002,
        S2C_SNAPSHOT    = 2000,
        S2C_FORCE_LOGOUT = 2005,
    }

    // Must match server NetworkConstants.h exactly
    public static class NetConst
    {
        public const int PACKET_SIZE_MIN = 8;
        public const int PACKET_SIZE_MAX = 1400;
        public const int UINT16_MIN      = 0;
        public const int UINT16_MAX      = 65535;
        public const int MATCH_ID_MIN    = 0;
        public const int MATCH_ID_MAX    = 1_000_000;
        public const int FLAGS_MIN       = 0;
        public const int FLAGS_MAX       = 255;
        public const int SEQ_MIN         = 0;
        public const int SEQ_MAX         = 255;
        public const int TICK_MIN        = 0;
        public const int TICK_MAX        = 65535;
        public const int DIR_MIN         = 0;
        public const int DIR_MAX         = 2;
        public const int SPEED_MIN       = 0;
        public const int SPEED_MAX       = 255;
        public const int FORCE_MIN       = 15;
        public const int FORCE_MAX       = 30;
    }

    // S2C raw structs — must match server #pragma pack(push,1) layout
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TankState
    {
        public uint  tankId;
        public float x, y, z;
        public float yaw;
        public short health;
        public byte  flags;          // bit0 = isAlive

        public bool IsAlive => (flags & 1) != 0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BulletState
    {
        public uint  bulletId;
        public uint  ownerId;   // tankId that fired this bullet
        public float x, y, z;
    }

    // S2C_SNAPSHOT raw header (not bit-packed — easier for Unity)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SnapshotHeader
    {
        public uint   matchId;
        public ushort opcode;          // = 2000
        public ushort serverTick;
        public ushort tankCount;
        public ushort localPlayerId;   // which tank belongs to this client
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ForceLogoutHeader
    {
        public uint matchId;
        public ushort opcode;             // = 2005
        public ushort code;               // e.g. 1003
        public ushort messageLen;         // UTF-8 bytes appended after header
        public uint disconnectAfterMs;    // e.g. 10000
    }

    // ── Packet builders ───────────────────────────────────────────────────────

    public static class PacketBuilder
    {
        // Write bit-packed PacketHeader (matches server PacketHeader::Serialize)
        private static void WriteHeader(BitWriter w, Opcode op, uint matchId,
                                        int size, byte seq = 0, ushort tick = 0)
        {
            w.WriteInt(size,          NetConst.PACKET_SIZE_MIN, NetConst.PACKET_SIZE_MAX);
            w.WriteInt((int)op,       NetConst.UINT16_MIN,      NetConst.UINT16_MAX);
            w.WriteInt((int)matchId,  NetConst.MATCH_ID_MIN,    NetConst.MATCH_ID_MAX);
            w.WriteInt(0,             NetConst.FLAGS_MIN,        NetConst.FLAGS_MAX);
            w.WriteInt(seq,           NetConst.SEQ_MIN,          NetConst.SEQ_MAX);
            w.WriteInt(tick,          NetConst.TICK_MIN,         NetConst.TICK_MAX);
        }

        public static byte[] BuildMove(uint matchId, int moveX, int moveZ,
                                       byte seq = 0, ushort tick = 0)
        {
            var w = new BitWriter(8);
            WriteHeader(w, Opcode.C2S_MOVE, matchId, 12, seq, tick);
            w.WriteInt(moveX + 1, NetConst.DIR_MIN,   NetConst.DIR_MAX);   // encode offset
            w.WriteInt(moveZ + 1, NetConst.DIR_MIN,   NetConst.DIR_MAX);
            w.WriteInt(0,         NetConst.SPEED_MIN,  NetConst.SPEED_MAX);
            return w.ToBytes();
        }

        public static byte[] BuildShoot(uint matchId, int launchForce = 20, byte seq = 0)
        {
            var w = new BitWriter(8);
            WriteHeader(w, Opcode.C2S_SHOOT, matchId, 8, seq);
            int force = System.Math.Max(NetConst.FORCE_MIN,
                        System.Math.Min(NetConst.FORCE_MAX, launchForce));
            w.WriteInt(force, NetConst.FORCE_MIN, NetConst.FORCE_MAX);
            return w.ToBytes();
        }
    }
}
