# Tank Server ↔ Unity Client Protocol

## Bit-packing algorithm (C++ và C# phải khớp chính xác)

### Cách bits được lưu trong buffer

```
Buffer = uint32[] words, little-endian
Scratch = uint64, LSB-first

WriteBits(value, n):
    scratch |= value << scratchBits
    scratchBits += n
    while scratchBits >= 32:
        buffer[word++] = (uint32)scratch   // flush 32 bits
        scratch >>= 32; scratchBits -= 32

ReadBits(n):
    while scratchBits < n:
        scratch |= buffer[word++] << scratchBits  // load 32 bits
        scratchBits += 32
    result = scratch & ((1 << n) - 1)
    scratch >>= n; scratchBits -= n

bits_required(min, max):
    range = max - min
    bits = 0; while (1 << bits) <= range: bits++

serialize_int write: WriteBits(value - min, bits_required(min, max))
serialize_int read:  ReadBits(bits_required(min, max)) + min
```

---

## Packet Header (bit-packed, cả 2 chiều)

| Field    | Type     | Range          | Bits |
|----------|----------|----------------|------|
| size     | uint16   | [8, 1400]      | 11   |
| opcode   | uint16   | [0, 65535]     | 16   |
| matchId  | uint32   | [0, 1000000]   | 20   |
| flags    | uint8    | [0, 255]       | 8    |
| seq      | uint8    | [0, 255]       | 8    |
| tick     | uint16   | [0, 65535]     | 16   |
| **Total**|          |                | **79 bits → 10 bytes padded** |

---

## C2S Packets (Unity → Server, bit-packed)

### C2S_MOVE (opcode=1001)
```
[Header: size=12, opcode=1001, matchId, flags=0, seq, tick=0]
[dirX:  uint8 [0,2]  → 0=left, 1=none, 2=right]  // 2 bits
[dirZ:  uint8 [0,2]  → 0=back, 1=none, 2=fwd]     // 2 bits
[speed: uint8 [0,255]]                              // 8 bits
```

### C2S_SHOOT (opcode=1002)
```
[Header: size=8, opcode=1002, matchId, flags=0, seq, tick=0]
// no payload
```

---

## S2C Packets (Server → Unity, raw binary — KHÔNG bit-packed)

Snapshot dùng `#pragma pack(push,1)` raw struct → Unity đọc bằng BinaryReader thẳng.

### S2C_SNAPSHOT (opcode=2000)
```
Offset  Size  Field
0       4     matchId    (uint32 LE)
4       2     opcode     (uint16 LE) = 2000
6       2     serverTick (uint16 LE)
8       2     tankCount  (uint16 LE)
10      N×23  TankState[]
  +0    4     tankId     (uint32)
  +4    4     x          (float)
  +8    4     y          (float)
  +12   4     z          (float)
  +16   4     yaw        (float)
  +20   2     health     (int16)
  +22   1     flags      (uint8, bit0=isAlive)
10+N×23 2     bulletCount (uint16 LE)
…       M×16  BulletState[]
  +0    4     bulletId   (uint32)
  +4    4     x          (float)
  +8    4     y          (float)
  +12   4     z          (float)
```

---

## Opcodes
```
C2S_LOGIN    = 1000  (không dùng nữa — match pre-registers players)
C2S_MOVE     = 1001
C2S_SHOOT    = 1002
S2C_SNAPSHOT = 2000
```
