# Packet Protocol – Tank Game Server

> Giao thức UDP, bit-packed bằng `WriteStream`/`ReadStream` (little-endian, word-aligned).
> Tham chiếu: `server_tank/include/Network/Packets.hpp`, `Opcode.hpp`, `NetworkConstants.h`

---

## Opcodes

| Hướng | Opcode (uint16) | Tên | Mô tả |
|-------|----------------|-----|-------|
| C→S | **1000** | `C2S_LOGIN`      | Client kết nối, yêu cầu spawn |
| C→S | **1001** | `C2S_MOVE`       | Gửi input di chuyển mỗi tick |
| C→S | **1002** | `C2S_SHOOT`      | Bắn đạn |
| S→C | **2000** | `S2C_LOGIN_RES`  | Server xác nhận, trả playerID |
| S→C | **2001** | `S2C_STATE_SYNC` | Snapshot toàn bộ thế giới |
| S→C | **2002** | `S2C_EVENT_SPAWN`| Tank hồi sinh |
| S→C | **2003** | `S2C_EVENT_HIT`  | Tank bị trúng đạn |

---

## Bit-Packing – nguyên tắc

Mỗi field được packed vào số bit tối thiểu dựa trên range:

```
bits_required(min, max) = ceil(log2(max - min + 1))
```

Bits được ghi liên tục vào buffer `uint32[]`, LSB-first. Khi hết word thì sang word tiếp theo.  
`~WriteStream()` tự gọi `Flush()` để write phần scratch còn lại.

---

## PacketHeader (mọi packet đều có)

```cpp
struct PacketHeader {
    uint16_t size;    // tổng byte của packet (header + payload)
    Opcode   opcode;  // uint16
    uint8_t  flags;   // reserved = 0
    uint8_t  seq;     // số thứ tự gói, vòng 0–255
    uint16_t tick;    // server tick hiện tại
};
```

### Bit layout

| Field    | Type     | Range      | Bits | Offset (bits) |
|----------|----------|-----------|------|--------------|
| `size`   | uint16   | 8 – 1400  | **11** | 0  |
| `opcode` | uint16   | 0 – 65535 | **16** | 11 |
| `flags`  | uint8    | 0 – 255   | **8**  | 27 |
| `seq`    | uint8    | 0 – 255   | **8**  | 35 |
| `tick`   | uint16   | 0 – 65535 | **16** | 43 |
| *(pad)*  |          |           | 5  | 59 |

**Tổng: 59 bits → 2 words → 8 bytes**

---

## C2S_LOGIN (opcode 1000)

```
[ PacketHeader ]   8 bytes
                   (không có payload)
```

`size = 8`

---

## C2S_MOVE (opcode 1001)

```
[ PacketHeader ]   8 bytes
[ PacketMovement ] tiếp ngay sau (bit 59 trở đi)
```

### PacketMovement

```cpp
struct PacketMovement {
    uint8_t dirX;   // 0 = trái,  1 = đứng yên, 2 = phải
    uint8_t dirZ;   // 0 = lùi,   1 = đứng yên, 2 = tiến
    uint8_t speed;  // 0–255, hiện tại server dùng cố định max
};
```

| Field   | Type   | Range   | Bits | Offset từ đầu header (bits) |
|---------|--------|---------|------|----------------------------|
| `dirX`  | uint8  | 0 – 255 | **8** | 59 |
| `dirZ`  | uint8  | 0 – 255 | **8** | 67 |
| `speed` | uint8  | 0 – 255 | **8** | 75 |
| *(pad)* |        |         | 5 | 83 |

**Tổng toàn gói: 83 bits → 3 words → 12 bytes** (`size = 12`)

#### Decode (server side)
```cpp
ClientInput ci;
ci.moveX = (int8_t)(dirX - 1);  // 0→-1  1→0  2→+1
ci.moveZ = (int8_t)(dirZ - 1);
```

---

## C2S_SHOOT (opcode 1002)

```
[ PacketHeader ]   8 bytes
                   (không có payload, hướng bắn = tank.yaw hiện tại)
```

`size = 8`

---

## S2C_STATE_SYNC (opcode 2001) – Binary blob (chưa bit-packed)

Server gọi `GameWorld::getSnapshot()` → trả `vector<uint8_t>` raw.

```
┌──────────────────────────────────────────┐
│ uint16  tankCount                        │  2 bytes
├──────────────────────────────────────────┤
│ TankState[0]                             │  23 bytes
│ TankState[1]                             │  23 bytes
│ ...                                      │
├──────────────────────────────────────────┤
│ uint16  bulletCount                      │  2 bytes
├──────────────────────────────────────────┤
│ BulletState[0]                           │  16 bytes
│ BulletState[1]                           │  16 bytes
│ ...                                      │
└──────────────────────────────────────────┘
```

### TankState (23 bytes, packed với `#pragma pack(push,1)`)

```cpp
struct TankState {
    uint32_t tankId;   // 4B
    float    x, y, z; // 4B × 3 = 12B – world position
    float    yaw;      // 4B – radians, quay quanh trục Y
    int16_t  health;   // 2B – [0, 100]
    uint8_t  flags;    // 1B – bit0 = isAlive
};  // tổng = 4+12+4+2+1 = 23 bytes
```

### BulletState (16 bytes)

```cpp
struct BulletState {
    uint32_t bulletId; // 4B
    float    x, y, z;  // 4B × 3 = 12B
};  // tổng = 16 bytes
```

---

## ClientInput (engine-internal, không đi qua mạng)

```cpp
struct ClientInput {
    int8_t  moveX     = 0;   // -1 / 0 / +1 (steering)
    int8_t  moveZ     = 0;   // -1 / 0 / +1 (throttle)
    float   turretYaw = 0.f; // radians, dùng cho hướng bắn
    bool    shoot     = false;
    uint8_t seq       = 0;
};
```

Được tạo ra bởi `PacketMovement::toClientInput()` sau khi decode.

---

## Ví dụ build packet (load_client)

```cpp
// MOVE: tiến thẳng, không bắn
uint8_t buf[32];
int n = PacketBuilder::buildMove(buf, sizeof(buf),
    /*seq=*/seqCounter++, /*tick=*/tickCounter,
    /*moveX=*/0, /*moveZ=*/1);   // moveZ=+1 = tiến
socket.sendTo(serverAddr, buf, n);  // gửi 12 bytes
```

---

## Ghi chú quan trọng

| Vấn đề | Chi tiết |
|--------|---------|
| Buffer size | `IoContext::MAX_SIZE = 1024` bytes – đủ cho mọi packet |
| Byte order | Little-endian (x86 native, không cần swap) |
| Reliability | UDP thuần, không có ACK, không retransmit |
| Anti-tunneling | Bullet dùng **swept sphere** vs static OBBs thay vì discrete check |
| Seq wraparound | `uint8_t seq` vòng 0→255→0, không gây lỗi logic vì server không kiểm tra thứ tự |
