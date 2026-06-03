# Phân tích & Xây dựng Cheat Tool — SE315.Q21 Demo Trụ cột 1

> **Mục đích**: Tài liệu học thuật. Ghi lại toàn bộ quá trình tư duy từ chưa biết gì → phân tích → khai thác được vị trí người chơi.

---

## Giai đoạn 0 — Xuất phát điểm

Có trong tay: một file `Tank Legends.exe` đang chạy trên Windows. Không có gì khác.

Mục tiêu đặt ra: **biết vị trí của tất cả người chơi khác trong trận**, ngay cả khi họ ngoài tầm nhìn.

Câu hỏi đầu tiên cần trả lời: *dữ liệu vị trí đó đang ở đâu?*

Có hai nơi có thể chứa nó:
1. **Trên mạng** — game nhận vị trí từ server qua network packet
2. **Trong RAM** — sau khi nhận, game lưu vào bộ nhớ để render

Cả hai đều là vector tấn công. Phân tích mạng trước vì nó cho biết cấu trúc dữ liệu — từ đó mới biết tìm gì trong RAM.

---

## Giai đoạn 1 — Phân tích mạng

### 1.1 Game dùng giao thức gì?

Bắt đầu với công cụ đơn giản nhất: xem game đang mở port nào.

```bash
netstat -ano | findstr Tank
# hoặc trên WSL2:
ss -unp | grep -i tank
```

Kết quả cho thấy game mở **UDP port 8080** — không phải TCP. Điều này có nghĩa:

- Không có handshake, không có connection state
- Mỗi packet độc lập
- Thường dùng cho game real-time vì latency thấp hơn TCP

### 1.2 Biết IP và port của server từ đâu?

Câu trả lời đơn giản hơn dự kiến — không cần đoán hay brute-force.

**Matchmaking API trả về thẳng trong response:**

```bash
# Bước 1: Login lấy JWT (tài khoản bình thường)
curl -X POST http://gateway:8080/api/auth/login \
  -d '{"username":"player1","password":"password123"}'
# → {"jwt": "eyJ...", "refreshToken": "..."}

# Bước 2: Tìm trận
curl -X POST http://gateway:8080/api/matchmaking/find \
  -H "Authorization: Bearer eyJ..."
```

Response trả về:
```json
{
  "matchId":    716578,
  "serverHost": "10.11.1.68",
  "serverPort": 8080,
  "playerId":   1
}
```

`serverHost` và `serverPort` nằm thẳng trong JSON plaintext. Bất kỳ client nào có tài khoản hợp lệ đều nhận được thông tin này — đây là thiết kế **by design**, vì client cần biết để kết nối UDP.

Trong `MatchmakingController.java`:
```java
// Khi game server ACK match.ready qua Kafka:
e.future().complete(ResponseEntity.ok(Map.of(
    "matchId",    matchId,
    "serverHost", serverHost,   // ← IP của Windows host
    "serverPort", serverPort,   // ← 8080
    "playerId",   slotId
)));
```

**Nếu không có tài khoản**, vẫn còn 2 cách khác:

| Cách | Thực hiện | Điều kiện |
|------|-----------|-----------|
| `netstat` / `ss` | Chạy khi game đang trong trận, xem UDP connection đến đâu | Có máy đang chạy game |
| Wireshark | Lọc UDP traffic, xem destination IP:port của packet game gửi đi | Cùng mạng LAN hoặc trên máy đó |

**Tóm lại**: Server IP/port không phải bí mật. Toàn bộ flow xác thực (JWT) chỉ bảo vệ **API gateway** — không bảo vệ **game server UDP**. Ai có IP và port đúng đều có thể gửi UDP packet đến server, kể cả không qua matchmaking.

---

### 1.3 Bit-packing chỉ áp dụng một chiều

Dùng Wireshark lọc `udp.port == 8080`. Ngay khi vào trận, xuất hiện hai luồng packet:

- **Client → Server**: ~20 packet/giây, nhỏ (~10-20 bytes) → đây là input người chơi
- **Server → Client**: ~20 packet/giây, lớn hơn (~100-200 bytes) → đây là state update

Packet lớn từ server gửi xuống với tần suất cố định chứa trạng thái trận đấu. Đây là thứ cần phân tích.

### 1.3 Bit-packing chỉ áp dụng một chiều

Giao thức có **hai chiều khác nhau hoàn toàn**:

**C2S — Client gửi lên Server (bit-packed):**
```
C2S_MOVE (1001) và C2S_SHOOT (1002) dùng BitWriter.
Mỗi field được nén theo range: bits_required(min, max) = ceil(log2(max-min+1))

Ví dụ dir_x ∈ [0,2] → chỉ cần 2 bit thay vì 8 bit.
Kết quả: một MOVE packet chỉ ~5 bytes thay vì ~20 bytes.
```

Bit-packing ở đây tối ưu bandwidth — client gửi 20 packet/giây nên tiết kiệm được đáng kể.

**S2C — Server gửi xuống Client (raw binary, KHÔNG bit-pack):**

Không tự suy đoán — có 4 nguồn bằng chứng độc lập:

**Bằng chứng 1 — Server source code (Match.cpp) — mạnh nhất:**
```cpp
// Comment ngay trong code server:
// "Raw S2C snapshot header — not bit-packed, Unity reads with BinaryReader"
#pragma pack(push, 1)
struct SnapshotHeader { ... };

// Hàm broadcastSnapshot():
std::memcpy(pkt.data(), &hdr, sizeof(hdr));
std::memcpy(pkt.data() + sizeof(hdr), body.data(), body.size());
_network.send(addr, pkt.data(), pkt.size());
```
`memcpy` trực tiếp struct vào packet buffer — không qua bất kỳ serializer nào. Đây là raw binary.

So sánh với cách server xử lý **C2S** (nhận từ client) trong cùng file:
```cpp
// handleMove() — C2S dùng ReadStream (bit-packed):
ReadStream rs(reinterpret_cast<const uint32_t*>(buf.data()), buf.size());
PacketHeader hdr{};
if (!hdr.Serialize(rs)) return;   // ← Serialize = đọc từng field theo số bits
PacketMovement pkt{};
if (!pkt.Serialize(rs)) return;
```
Cùng một file, hai hướng xử lý khác nhau hoàn toàn:
- **S2C**: `memcpy` struct → raw binary
- **C2S**: `ReadStream` + `Serialize` → bit-packed

**Bằng chứng 2 — Cách client parse (TankNetClient.cs):**
```csharp
snap.Tanks[i] = BytesToStruct<TankState>(data, offset);
offset += tankSize;  // += 26 bytes cố định
```
`Marshal.PtrToStructure<T>` là raw memory copy — pin byte array vào memory, cast pointer thẳng sang struct. Không thể dùng cách này nếu data là bit stream.

**Bằng chứng 3 — Attribute trên struct (TankProtocol.cs):**
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TankState { ... }
```
`Pack = 1` = layout bộ nhớ khớp 1:1 với wire format. Vô nghĩa nếu data là bit-packed.

**Bằng chứng 4 — Xác nhận từ Wireshark:**
Lấy packet S2C, đọc 26 bytes TankState từ offset 16:
```
Bytes 0-3  (tankId):  01 00 00 00 = 1       ← khớp ID trong game
Bytes 4-7  (x):       9A 99 B9 41 = 23.2f   ← tọa độ hợp lý
Bytes 8-11 (y):       CD CC 4C 3F = 0.8f    ← độ cao hợp lý
Bytes 12-15 (z):      AE 47 F3 42 = 121.6f  ← tọa độ hợp lý
Bytes 16-19 (yaw):    DB 0F 49 40 = 3.14f   ← ~π radian
Bytes 20-21 (health): 64 00      = 100      ← HP full
Bytes 22    (flags):  01         = isAlive
```
Nếu bit-packed, đọc theo byte boundary ra số vô nghĩa. Các giá trị khớp xác nhận raw binary.

**Kết luận**: 4 nguồn đều hội tụ. S2C_SNAPSHOT là raw binary có chủ ý — server dùng `memcpy`, client dùng `Marshal.PtrToStructure`, cả hai đều không có serializer bit-level ở giữa.

Vì vậy, lấy packet S2C từ Wireshark và đọc thẳng hex là được:

```
Offset  Hex                              Giải thích
00000   D2 E8 0A 00                      matchId = 0x000AE8D2 = 716754
00004   D0 07                            opcode  = 0x07D0 = 2000
00006   77 E6                            serverTick
00008   02 00                            tankCount = 2
0000A   11 00                            localPlayerId = 17
0000C   70 26                            timeRemainingTenths
0000E   02 00                            tankCount (body prefix, lặp lại)
00010   [TankState đầu tiên bắt đầu ở đây, 26 bytes]
```

Packet này **không mã hoá, không nén**, đọc trực tiếp từng field.

### 1.4 Xác nhận cấu trúc bằng source code Unity

Game được viết bằng Unity, có thể đọc C# script trong thư mục `Assets/`. Tìm file `TankProtocol.cs`:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SnapshotHeader          // 14 bytes
{
    public uint   matchId;            // 4
    public ushort opcode;             // 2  = 2000
    public ushort serverTick;         // 2
    public ushort tankCount;          // 2
    public ushort localPlayerId;      // 2
    public ushort timeRemainingTenths;// 2
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TankState               // 26 bytes
{
    public uint  tankId;              // 4
    public float x, y, z;            // 12  ← vị trí 3D
    public float yaw;                 // 4   ← góc xoay (radian)
    public short health;              // 2
    public byte  flags;               // 1   bit0=isAlive
    public ushort score;              // 2
    public byte   placement;          // 1
}
```

**Phát hiện quan trọng**: Server gửi `TankState` của **tất cả** người chơi trong trận, không phân biệt đây là địch hay đồng minh, trong tầm nhìn hay không. Client nhận toàn bộ để render.

Đây là lỗ hổng thiết kế cơ bản: game cần data của địch để hiển thị khi địch lọt vào tầm nhìn, nên server phải gửi sớm. Nhưng việc gửi **luôn luôn**, ngay cả khi địch đang ẩn sau tường, là thừa thông tin không cần thiết.

### 1.5 Cấu trúc đầy đủ của packet S2C_SNAPSHOT

```
Byte 0-3:   matchId (uint32)
Byte 4-5:   opcode = 2000 (uint16)
Byte 6-7:   serverTick (uint16)
Byte 8-9:   tankCount (uint16)
Byte 10-11: localPlayerId (uint16)
Byte 12-13: timeRemainingTenths (uint16)
Byte 14-15: tankCount lặp lại (uint16) ← body prefix, client skip
Byte 16+:   TankState[0], TankState[1], ...  (26 bytes mỗi cái)
```

Lý do `tankCount` xuất hiện hai lần: server ghép `SnapshotHeader` vào trước `body`, mà `body` bắt đầu bằng `tankCount`. Client khi parse phải skip 2 bytes này (`offset = hdrSize + 2`).

---

## Giai đoạn 2 — Xác định mục tiêu trong RAM

Biết cấu trúc packet rồi. Câu hỏi tiếp theo: *tìm dữ liệu này ở đâu trong RAM của game?*

### 2.1 Tại sao không dùng địa chỉ tĩnh (static address)?

Cách hack truyền thống: dùng Cheat Engine tìm địa chỉ của biến, hardcode địa chỉ đó vào tool.

Cách này **không hoạt động** với Unity vì:
- Unity dùng **managed heap** — GC (Garbage Collector) tự động compact heap, di chuyển object
- Mỗi lần GC chạy, địa chỉ thay đổi
- Mỗi lần khởi động lại game, địa chỉ thay đổi
- Không có base address cố định để tính offset

### 2.2 Chiến lược: Pattern scan

Thay vì tìm địa chỉ, tìm **pattern đặc trưng** của data. Logic:

> "Tôi biết packet snapshot có opcode = 2000 tại offset +4. Nếu tôi quét toàn bộ RAM và tìm vùng nhớ nào có bytes `D0 07` tại một offset, và các bytes xung quanh khớp với cấu trúc SnapshotHeader, thì đó chính là buffer đang chứa snapshot."

Tính đặc trưng của pattern:
- `opcode = 2000` (giá trị cụ thể, ít xuất hiện ngẫu nhiên)
- `tankCount ∈ [1, 8]` (giới hạn hợp lý)
- `localPlayerId ∈ [1, 255]` (không phải 0, không vượt quá 255)
- Các float `x, y, z` trong bounds `[-3000, 3000]` (giới hạn map)
- `health ∈ [0, 200]`

Kết hợp 5 điều kiện này, xác suất false positive cực thấp.

### 2.3 Quét RAM như thế nào?

Windows API cung cấp đủ công cụ:

```
Bước 1: OpenProcess(PROCESS_ALL_ACCESS, pid)
         → lấy handle đọc/ghi RAM của process khác
         Điều kiện: chạy cùng user hoặc Administrator

Bước 2: VirtualQueryEx(handle, address, &mbi)
         → lấy thông tin từng vùng nhớ (address, size, protection flags)
         → lặp qua toàn bộ virtual address space

Bước 3: Lọc vùng nhớ:
         - State == MEM_COMMIT      (đã được cấp phát)
         - Protect & PAGE_READWRITE (có thể đọc và ghi)
         - RegionSize < 32MB        (bỏ qua vùng quá lớn như texture atlas)

Bước 4: ReadProcessMemory(handle, base, buffer, size)
         → đọc toàn bộ vùng nhớ đã lọc vào buffer local

Bước 5: Tìm kiếm pattern trong buffer:
         → duyệt từng byte, kiểm tra xem offset +4 có == 2000 không
         → nếu có, validate toàn bộ header + TankState
```

### 2.4 Vấn đề: nhiều candidate

Khi quét RAM, có thể tìm thấy **nhiều vùng** thỏa pattern — vì Unity giữ nhiều bản copy của packet trong memory (receive buffer, parsed object, cũ chưa GC...).

Giải pháp: **chọn candidate có `serverTick` cao nhất** — đó là snapshot được ghi vào gần nhất, tức là mới nhất.

```cpp
// Không return ngay khi tìm thấy — tiếp tục scan toàn bộ
// Chỉ lưu lại nếu tick mới hơn candidate trước
if (best.valid && hdr.serverTick <= best.tick) continue;
// → sau khi scan xong, best chứa snapshot mới nhất
```

### 2.5 Vấn đề đã gặp: offset sai dẫn đến dữ liệu sai

Lần đầu implement, tool đọc được matchId và tick hợp lệ nhưng tọa độ toàn 0. Debug theo từng bước:

**Lỗi 1**: `SnapshotHeader` trong C++ thiếu field `timeRemainingTenths` (2 bytes) so với C#.
```
C++ struct (sai): 12 bytes → TankState bắt đầu tại offset 12
C# struct (đúng): 14 bytes → TankState bắt đầu tại offset 14
Kết quả: đọc sai offset 2 bytes → tọa độ nhảm
```

**Lỗi 2**: Sau khi sửa header, vẫn sai. Đọc lại `TankNetClient.cs`:
```csharp
// "body bắt đầu bằng uint16 tankCount (trùng với hdr.tankCount), skip nó"
int offset = hdrSize + 2;   // ← phải skip thêm 2 bytes body prefix
```

```
Header: 14 bytes
Body prefix (tankCount lặp): 2 bytes
TankState bắt đầu tại: 14 + 2 = 16  ← offset đúng
```

**Bài học**: Dù đã đọc source C#, vẫn cần trace lại từng dòng code parse để hiểu chính xác offset. Struct definition và wire format không phải lúc nào cũng đồng nhất.

---

## Giai đoạn 3 — Khai thác dữ liệu vị trí

### 3.1 Dữ liệu đọc được

Sau khi scan và parse đúng, mỗi `TankState` trả về:

```cpp
struct TankInfo {
    uint32_t  id;       // tankId
    float     x, z;    // vị trí trên mặt phẳng (y là độ cao, ít quan trọng)
    float     yaw;     // góc xoay tháp pháo, radian
    int16_t   hp;      // máu
    bool      alive;   // còn sống không
    bool      isMe;    // đây có phải tank của mình không
    float     dist;    // khoảng cách từ mình đến tank này
    uintptr_t yawAddr; // địa chỉ RAM của field yaw (dùng cho aimbot)
};
```

`isMe` được xác định bằng cách so sánh `tankId == localPlayerId` từ header — server luôn cho biết ID của người chơi đang nhận packet.

### 3.2 Hiển thị ESP (Extra Sensory Perception)

Đọc snapshot mỗi 100ms, render lên overlay:

**Bảng thông tin:**
```
ID    X         Z         HP    Dist    Dir   Status
1     5.8       30.5      100   0.0     -     [ME]
2     8.9       38.0      75    8.1     NE    [ENEMY]
```

**Tính hướng compass:**
```
dx = enemy.x - my.x
dz = enemy.z - my.z
angle = atan2(dx, dz) * 180/π   → độ, 0° = Bắc
sector = int((angle + 22.5) / 45) % 8
→ map sang: N, NE, E, SE, S, SW, W, NW
```

**Mini-map 2D:**
```
pixel_x = MAP_CENTER + (tank.x - my.x) / RANGE * MAP_RADIUS
pixel_y = MAP_CENTER - (tank.z - my.z) / RANGE * MAP_RADIUS
                       ↑ trục Z ngược chiều trục Y màn hình
```

### 3.3 Overlay trong suốt đè lên game

Dùng Windows GDI với `WS_EX_LAYERED`:

```
1. Tạo cửa sổ popup, WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT
2. SetLayeredWindowAttributes(hwnd, RGB(0,128,0), 0, LWA_COLORKEY)
   → pixel màu RGB(0,128,0) = trong suốt
3. Vẽ background toàn bộ màu chroma key → trở thành trong suốt
4. Vẽ text/shape màu khác → hiển thị trên game
5. WS_EX_TRANSPARENT → click chuột xuyên qua, không cản game
6. Mỗi 10ms: GetWindowRect(gameWnd) → SetWindowPos(overlay) → bám theo game
```

---

## Giai đoạn 4 — Mở rộng: Aimbot

### 4.1 Từ "đọc vị trí" đến "tự động nhắm"

Có tọa độ của địch (`enemy.x`, `enemy.z`) và của mình (`my.x`, `my.z`). Muốn tank tự xoay về hướng địch.

Xem `GameManager.cs` để hiểu game dùng `yaw` như thế nào:

```csharp
// GameManager.cs, dòng ~313
var rot = Quaternion.Euler(0, ts.yaw * Mathf.Rad2Deg, 0);
go.transform.rotation = serverRot;
```

Server broadcast `yaw` trong `TankState`, client apply nó vào rotation của tank. Nếu ghi đè được `yaw` trong RAM trước khi client gửi input lên server, tank sẽ xoay theo giá trị mới.

### 4.2 Tính góc nhắm

```
targetYaw = atan2(enemy.x - my.x, enemy.z - my.z)
```

Đây là góc theo chiều kim đồng hồ từ trục +Z (Bắc), đơn vị radian — khớp với format `TankState.yaw`.

**Lỗi đã gặp**: Lần đầu ghi degree thay vì radian. `atan2` trả về `0.15 rad` (~8.6°), nhân thêm `180/π` thành `8.6` — tank xoay chỉ 8.6° trong khi cần 8.6° * Rad2Deg = 492°. Kết quả: tank xoay lung tung.

Fix: không nhân Rad2Deg, ghi thẳng kết quả `atan2`.

### 4.3 Ghi vào RAM

`yawAddr` đã được tính chính xác khi scan:

```cpp
yawAddr = base + offset_trong_buf + sizeof(SnapHdr) + 2
        + tank_index * sizeof(TankStateRaw)
        + offsetof(TankStateRaw, yaw);

WriteProcessMemory(g_handle, (LPVOID)yawAddr, &targetYaw, 4, &written);
```

Ghi mỗi 16ms (~60Hz) vào field `yaw` của tank mình → tank luôn hướng về địch.

### 4.4 Tại sao server chấp nhận?

Vòng lặp:
```
1. Aimbot ghi yaw mới vào RAM client  (16ms interval)
2. Client đọc transform hiện tại → gửi C2S_MOVE lên server
3. Server nhận yaw từ client, update TankState
4. Server broadcast S2C_SNAPSHOT (50ms interval) với yaw mới
5. Client apply yaw từ snapshot → tank xoay theo
6. Quay lại bước 1
```

Server không verify tốc độ xoay — không có giới hạn "tank chỉ xoay tối đa X độ/giây". Bất kỳ giá trị yaw nào client gửi đều được chấp nhận.

---

## Giai đoạn 5 — Tại sao bị phát hiện và cách phòng

### 5.1 Dấu hiệu ở user-mode

| API bị gọi | Lý do đáng ngờ |
|-----------|----------------|
| `OpenProcess(PROCESS_ALL_ACCESS, game_pid)` | Không có ứng dụng hợp lệ nào cần full access vào RAM game |
| `VirtualQueryEx` lặp qua toàn bộ address space | Pattern đặc trưng của memory scanner |
| `ReadProcessMemory` trên vùng heap game | Debug tools dùng cách này, nhưng không trong khi game đang chạy |
| `WriteProcessMemory` vào offset trong heap | Không có use case hợp lệ |

### 5.2 Kernel-level anti-cheat (Vanguard, EAC)

Driver cài ở Ring 0, dùng `ObRegisterCallbacks` để intercept mọi lời gọi `OpenProcess`. Khi tool gọi `OpenProcess(PROCESS_ALL_ACCESS, game_pid)`, callback của driver chạy **trước khi** lời gọi hoàn thành — driver có thể từ chối, hoặc ghi log, hoặc terminate luôn process đang gọi.

Đây là lý do Vanguard trigger **VAN-79** khi tool chạy: không phải vì tool tương tác với Valorant, mà vì hành vi `OpenProcess + VirtualQueryEx + ReadProcessMemory` đủ để trigger pattern detection, dù target process là Unity game khác.

### 5.3 Phòng chống thiết kế (server-side)

| Biện pháp | Tác dụng |
|-----------|----------|
| **Fog of War** | Không gửi `TankState` của địch đang ở ngoài tầm nhìn → ESP không có gì để đọc |
| **Rotation velocity check** | Server từ chối `yaw` thay đổi >N rad/tick → aimbot bị giới hạn |
| **Server-side physics** | Server tự tính trajectory đạn từ vị trí + hướng hợp lệ, không tin client |
| **Statistical detection** | Theo dõi win rate, reaction time, snap angle → flag tài khoản |

---

## Appendix — Đọc raw Wireshark hex dump

Khi bắt được packet thực tế, cần bóc từng layer mạng trước khi đến payload.

### A.1 Bóc layer mạng

Packet Wireshark là full Ethernet frame, không phải payload thuần:

```
0000   86 03 77 72 d4 e0 4e 03 4f f4 e3 95 08 00 45 00
0010   00 62 f3 47 00 00 7f 11 f2 a2 0a 0b 01 44 c0 a8
0020   89 a9 1f 90 ec 48 00 4e 4d 1d 2c ef 0a 00 d0 07
0030   7e 79 02 00 02 00 d5 08 02 00 01 00 00 00 06 66
0040   11 c0 00 00 00 00 b1 df f0 41 00 00 00 00 64 00
0050   01 00 00 02 02 00 00 00 51 3c 0b 42 00 00 00 00
0060   bc 64 ce 41 46 90 3b 40 64 00 01 00 00 02 00 00
```

```
Bytes  0-13:   Ethernet header (14 bytes)
               86 03 77 72 d4 e0 = dst MAC
               4e 03 4f f4 e3 95 = src MAC
               08 00             = EtherType IPv4

Bytes 14-33:   IPv4 header (20 bytes, IHL=5)
               45 = version 4, header 20 bytes
               7f 11 = TTL=127, proto=17(UDP)
               0a 0b 01 44 = src 10.11.1.68  ← game server
               c0 a8 89 a9 = dst 192.168.137.169 ← client

Bytes 34-41:   UDP header (8 bytes)
               1f 90 = src port = 8080  ← server gửi
               ec 48 = dst port = 60488 (ephemeral)
               00 4e = UDP length = 78 bytes

Bytes 42-111:  UDP payload (78 - 8 = 70 bytes)  ← đây mới là game data
```

### A.2 Parse payload bằng struct đã biết

Payload bắt đầu tại byte 42 (offset 0x2A trong hex dump).

```
Offset  Bytes            Giá trị              Field
+00     2c ef 0a 00      716588               matchId (uint32 LE)
+04     d0 07            2000 ✓               opcode  (uint16 LE)
+06     7e 79            31102                serverTick
+08     02 00            2                    tankCount
+0a     02 00            2                    localPlayerId ← tôi là player 2
+0c     d5 08            226.1s còn lại       timeRemainingTenths / 10
+0e     02 00            [skip]               body prefix (tankCount lặp)

── TankState[0] ──────────────────────────────────────────────
+10     01 00 00 00      1                    tankId
+14     06 66 11 c0      -2.27f               x
+18     00 00 00 00      0.0f                 y (tank trên sàn)
+1c     b1 df f0 41      30.1f                z
+20     00 00 00 00      0.0f rad             yaw (hướng Bắc)
+24     64 00            100                  health
+26     01               bit0=1 → isAlive     flags
+27     00 00            0                    score
+29     02               2                    placement

── TankState[1] ──────────────────────────────────────────────
+2a     02 00 00 00      2                    tankId
+2e     51 3c 0b 42      34.8f                x
+32     00 00 00 00      0.0f                 y
+36     bc 64 ce 41      25.7f                z
+3a     46 90 3b 40      2.93f rad            yaw (~168°, hướng SW)
+3e     64 00            100                  health
+40     01               isAlive              flags
+41     00 00            0                    score
+43     02               2                    placement

── Sau TankState ──────────────────────────────────────────────
+44     00 00            0                    bulletCount = 0
```

### A.3 Cách giải một float IEEE 754 tay

Ví dụ: bytes `06 66 11 c0` → float x của tank[0]

```
Bước 1: Little-endian → đọc ngược byte order
        06 66 11 c0 → 0xC0116606

Bước 2: Tách 3 phần của IEEE 754 single (32-bit):
        Binary: 1 10000000 00010001011001100000110
                │ └───┬───┘ └────────┬────────────┘
               sign  exp(8)      mantissa(23)

Bước 3: Tính từng phần:
        sign     = 1  → âm
        exp      = 10000000₂ = 128, actual = 128 - 127 = 1
        mantissa = 0x116606 / 0x800000 = 0.1354

Bước 4: Ghép lại:
        value = (-1)¹ × 2¹ × (1 + 0.1354) = -2.27
```

### A.4 Nếu không có source code — làm sao biết struct?

Không có `TankProtocol.cs`, vẫn tìm ra struct bằng cách:

**Quan sát nhiều packet từ cùng một session:**

| Bytes | Thay đổi như thế nào | Suy ra |
|-------|----------------------|--------|
| 0-3 | Không đổi trong cả trận | matchId (ID trận, constant) |
| 4-5 | Luôn `D0 07` | opcode = 2000 (magic number) |
| 6-7 | Tăng đều +1 mỗi packet | serverTick (counter) |
| 8-9 | Không đổi | tankCount |
| 10-11 | Không đổi | localPlayerId |
| 12-13 | Giảm dần theo thời gian | timeRemaining |
| 14-15 | Bằng bytes 8-9 | body prefix (duplicate) |

**Tiếp theo với TankState — tìm float:**

4 bytes liên tiếp có giá trị trong khoảng [-3000, 3000] khi decode IEEE 754 → đây là tọa độ. Thử di chuyển tank và xem bytes nào thay đổi tương ứng → xác định x, z. Byte gần cuối block (1 byte) chỉ có 2 giá trị (0x00/0x01) → flags.

Quá trình này mất vài giờ. Có source code → vài phút.

---

## Appendix B — Phương pháp 2: Suy luận struct từ nhiều gói tin (delta analysis)

> Phương pháp 1 (Appendix A) dùng khi đã có nguồn gốc struct. Phương pháp 2 này áp dụng khi **không có source code** — chỉ có Wireshark.

### B.1 Nguyên lý

Thu thập N gói tin từ cùng một session. So sánh từng byte cùng offset. Mỗi byte có một "profile biến đổi":

| Kiểu thay đổi | Suy ra |
|---------------|--------|
| Không bao giờ đổi | Constant — ID, magic number, opcode |
| Tăng +1 mỗi gói | Monotonic counter — sequence, tick |
| Giảm dần theo giây | Timer / countdown |
| Đổi khi entity di chuyển | Tọa độ / góc xoay |
| Chỉ có giá trị 0 hoặc 1 | Boolean / flags byte |
| Đổi khi nhận sát thương | Health |

Sau khi gom các byte có cùng "kiểu thay đổi", dùng giá trị để suy luận độ rộng field (uint8, uint16 LE, float…).

---

### B.2 Dataset: 3 gói tin liên tiếp (Wireshark)

3 UDP snapshot từ server → client, IPv4 ID lần lượt `f3 2e`, `f3 2f`, `f3 30` — 3 datagram liên tiếp, cách nhau 50ms.

Sau khi strip Ethernet(14) + IPv4(20) + UDP(8), **payload 70 bytes** bắt đầu từ offset `0x2A`:

```
         offset:  00 01 02 03  04 05  06 07  08 09  0A 0B  0C 0D  0E 0F

Tick 31080 (P1):  2c ef 0a 00  d0 07  68 79  02 00  02 00  dc 08  02 00
Tick 31081 (P2):  2c ef 0a 00  d0 07  69 79  02 00  02 00  db 08  02 00
Tick 31082 (P3):  2c ef 0a 00  d0 07  6a 79  02 00  02 00  db 08  02 00
Delta (P2-P1):    .. .. .. ..  .. ..  +1 ..  .. ..  .. ..  -1 ..  .. ..
Delta (P3-P2):    .. .. .. ..  .. ..  +1 ..  .. ..  .. ..  .. ..  .. ..

         offset:  10..1F  20..2F  30..3F  40..2F  (bytes 16-67 = TankState × 2)
Tick 31080 (P1):  [identhical across all 3 packets — 52 bytes game data]
Tick 31081 (P2):  [identical]
Tick 31082 (P3):  [identical]

         offset:  44 45  (bytes 68-69 = trailing padding)
Tất cả:           00 00  [không đổi]
```

**Tổng hợp: chỉ 2 vị trí thay đổi trong toàn bộ 70 bytes.**

---

### B.3 Phân tích từng vùng thay đổi

#### Bytes 06–07: giá trị `68 79` → `69 79` → `6a 79`

Byte 06 tăng +1 mỗi gói, byte 07 không đổi. Decode LE uint16:

```
P1: 0x7968 = 31080
P2: 0x7969 = 31081
P3: 0x796a = 31082
```

**Kết luận**: đây là **monotonic counter**, tăng đúng 1 mỗi packet → `serverTick`.

Từ khoảng cách giữa gói tin (~50ms) và bước nhảy = 1, suy ra **server broadcast 20 Hz**.

#### Bytes 0C–0D: giá trị `dc 08` → `db 08` → `db 08`

Byte 0C giảm 1 giữa P1 và P2, rồi giữ nguyên ở P3. Decode LE uint16:

```
P1: 0x08dc = 2268
P2: 0x08db = 2267
P3: 0x08db = 2267 (không đổi)
```

**Kết luận**: giá trị giảm dần theo thời gian → **countdown timer**. Đơn vị = 0.1 giây (2268 ÷ 10 = 226.8 giây còn lại). Decrement xảy ra mỗi 2 tick (không phải mỗi tick) → server cập nhật timer mỗi 0.2 giây.

#### Bytes 00–05: không đổi

```
2c ef 0a 00  d0 07
```

- `2c ef 0a 00` = LE uint32 = **716588** → constant cả trận → `matchId`
- `d0 07` = LE uint16 = **2000** → constant → protocol opcode (`S2C_SNAPSHOT`)

#### Bytes 08–0B: `02 00  02 00` — không đổi

Hai uint16 LE = 2 và 2 → `tankCount = 2`, `localPlayerId = 2`.

#### Bytes 0E–0F: `02 00` — không đổi, bằng bytes 08–09

Giá trị trùng với `tankCount` → đây là **body prefix** (duplicate count trước phần TankState).

#### Bytes 10–43 (52 bytes TankState): **không đổi**

Cả 52 bytes game data giống hệt nhau qua 3 tick → hai tank đứng yên hoàn toàn trong 150ms này.

---

### B.4 Header map đúc kết từ delta analysis

```
Offset  Size  Kiểu biến đổi          Kết luận
──────────────────────────────────────────────────────────
00–03   4     Constant               matchId (uint32 LE)
04–05   2     Constant (= 2000)      opcode  (uint16 LE)
06–07   2     +1 mỗi packet          serverTick (uint16 LE)
08–09   2     Constant (= 2)         tankCount (uint16 LE)
0A–0B   2     Constant (= 2)         localPlayerId (uint16 LE)
0C–0D   2     Giảm dần, đơn vị 0.1s  timeRemainingTenths (uint16 LE)
0E–0F   2     = bytes 08–09          body prefix (duplicate tankCount)
10–43   52    Game entity data       TankState × 2 (xem bên dưới)
44–45   2     Luôn 0x0000            padding
```

Tổng header thực sự: **14 bytes** (00→0D). Body prefix thêm 2 bytes → TankState bắt đầu ở **offset 16**.

---

### B.5 Cách xác định TankState nếu không có source code

Với 3 packet tĩnh này, bytes 10–43 không đổi nên chỉ biết chúng là "entity data". Để suy luận TankState, cần **thêm packet từ các tình huống khác**:

**Bước 1 — Tìm field nào là tọa độ:**

| Hành động | Bytes nào thay đổi | Suy ra |
|-----------|-------------------|--------|
| Di chuyển tank +X | 4 bytes tại offset 10→13 | field x (float) |
| Di chuyển tank +Z | 4 bytes tại offset 18→1B | field z (float) |
| Tank bị bắn | 2 bytes tại offset 24→25 | field health (int16) |
| Tank xoay | 4 bytes tại offset 1C→1F | field yaw (float) |

**Bước 2 — Xác định float bằng range check:**

4 bytes có thể là float nếu khi decode IEEE 754 ra giá trị nằm trong range hợp lý của game world (ví dụ [-200, 200] cho tọa độ). So sánh nhiều packet → value thay đổi smooth theo chuyển động → đây là float.

**Bước 3 — Xác định flags byte:**

Byte chỉ nhận 2 giá trị (`00` và `01` hoặc bitmask) → flags. Trong 3 packet này: byte offset 26 luôn = `01` (alive).

**Bước 4 — Xác định ranh giới giữa 2 TankState:**

Nếu có 2 tank, TankState đầu chiếm N bytes → TankState thứ hai bắt đầu ở offset 16+N. Thay đổi tank 1 → chỉ bytes trong [16, 16+N) đổi. Di chuyển chỉ tank 2 → chỉ bytes sau đó đổi. Từ đây đo được N = 26 bytes.

---

### B.6 Xác nhận: kết quả decode game state từ 3 packet

Áp dụng struct đã suy luận vào payload:

**Tank 1** (tankId = 1):
```
Bytes 10–13: 01 00 00 00  → tankId = 1
Bytes 14–17: 06 66 11 c0  → x = 0xC0116606 = -2.27
Bytes 18–1B: 00 00 00 00  → y = 0.0
Bytes 1C–1F: b1 df f0 41  → z = 0x41F0DFB1 ≈ +30.11
Bytes 20–23: 00 00 00 00  → yaw = 0.0 rad (hướng +Z)
Bytes 24–25: 64 00        → health = 100
Byte  26:    01           → flags = alive
Bytes 27–28: 00 00        → score = 0
Byte  29:    02           → placement = 2
```

**Tank 2** (tankId = 2):
```
Bytes 2A–2D: 02 00 00 00  → tankId = 2
Bytes 2E–31: 51 3c 0b 42  → x = 0x420B3C51 ≈ +34.81
Bytes 32–35: 00 00 00 00  → y = 0.0
Bytes 36–39: bc 64 ce 41  → z = 0x41CE64BC ≈ +25.78
Bytes 3A–3D: 46 90 3b 40  → yaw = 0x403B9046 ≈ 2.93 rad ≈ 168°
Bytes 3E–3F: 64 00        → health = 100
Byte  40:    01           → flags = alive
Bytes 41–42: 00 00        → score = 0
Byte  43:    02           → placement = 2
```

Khoảng cách 2 tank: `√((34.81−(−2.27))² + (25.78−30.11)²)` ≈ **37.3 units**. Tank 2 đang quay lưng lại (168°) so với hướng về tank 1.

---

### B.7 Tại sao UDP checksum của P1 và P2 bằng nhau?

```
P1 → P2: byte 06 tăng 1 (68→69), byte 0C giảm 1 (dc→db)
```

UDP checksum dùng **1's complement**. `+1` và `−1` triệt tiêu nhau → checksum không đổi (`5c 1d`).

```
P2 → P3: byte 06 tăng 1 (69→6a), byte 0C không đổi
```

Net = +1 → checksum giảm 1: `5c 1d` → `5b 1d`. ✓

Đây là cách kiểm tra nhanh xem struct parse có đúng không: nếu bạn đã biết hai field thay đổi và checksum hành xử đúng như dự đoán → struct map là chính xác.

---

### B.8 Từ struct đã biết → tấn công RAM

Wireshark chỉ đọc được packet trên dây. Nhưng **game process lưu lại nội dung packet vào RAM** sau khi parse — đó là snapshot buffer mà Unity dùng để render. Khai thác RAM cho phép:

- **Đọc**: lấy vị trí địch theo thời gian thực dù Wireshark đã đóng
- **Ghi**: sửa field ngay trong bộ nhớ trước khi game đọc frame tiếp theo

#### Tìm buffer trong RAM

Game không lưu snapshot ở địa chỉ cố định — Unity GC di chuyển object. Dùng pattern scan:

```
Điều kiện tìm:
  - 4 bytes tại offset +4 đọc ra = 0x07D0 (opcode 2000)    ← dấu hiệu nhận dạng
  - 2 bytes tại offset +8 đọc ra ∈ [1, 8]                  ← tankCount hợp lý
  - 2 bytes tại offset +0 đọc ra ∈ [0, 2000000]             ← matchId hợp lý
```

Trong Windows, dùng `VirtualQueryEx` để liệt kê tất cả vùng nhớ của tiến trình, sau đó `ReadProcessMemory` để đọc từng vùng và kiểm tra pattern. Nếu nhiều vùng match (GC copy cũ), lấy vùng có `serverTick` cao nhất.

```cpp
// Mở tiến trình game
HANDLE hProc = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION,
                           FALSE, pid);

// Quét toàn bộ RAM
MEMORY_BASIC_INFORMATION mbi;
uintptr_t addr = 0;
while (VirtualQueryEx(hProc, (LPCVOID)addr, &mbi, sizeof(mbi))) {
    if (mbi.State == MEM_COMMIT && mbi.Type == MEM_PRIVATE) {
        std::vector<uint8_t> buf(mbi.RegionSize);
        ReadProcessMemory(hProc, mbi.BaseAddress, buf.data(), mbi.RegionSize, nullptr);
        // tìm pattern opcode 2000 trong buf...
    }
    addr += mbi.RegionSize;
}
```

#### Đọc field sau khi tìm được buffer

Từ delta analysis (Appendix B.4), đã biết:
- Header = 14 bytes, body prefix = 2 bytes
- TankState bắt đầu ở offset **16** từ đầu payload
- Trong mỗi TankState 26 bytes: `x` tại +4, `z` tại +12, `yaw` tại +16, `health` tại +20

```cpp
// base = địa chỉ đầu snapshot buffer trong RAM
uintptr_t tankBase = base + 16;          // bỏ header + prefix
uintptr_t xAddr    = tankBase + 4;       // float x
uintptr_t zAddr    = tankBase + 12;      // float z
uintptr_t yawAddr  = tankBase + 16;      // float yaw (radians)
uintptr_t hpAddr   = tankBase + 20;      // int16 health

float x, z, yaw;  int16_t hp;
ReadProcessMemory(hProc, (LPCVOID)xAddr,   &x,   4, nullptr);
ReadProcessMemory(hProc, (LPCVOID)zAddr,   &z,   4, nullptr);
ReadProcessMemory(hProc, (LPCVOID)yawAddr, &yaw, 4, nullptr);
ReadProcessMemory(hProc, (LPCVOID)hpAddr,  &hp,  2, nullptr);
```

#### Ghi đè field (aimbot)

Ghi chỉ có nghĩa với field mà **game đọc từ RAM trước mỗi frame render**. Tọa độ của tank khác thì server là nguồn sự thật — ghi vào RAM chỉ ảnh hưởng đến render cục bộ. Nhưng `yaw` của tank mình thì game submit lên server qua C2S_MOVE → ghi đè trước khi game đọc sẽ thay đổi hành vi thực sự.

```cpp
// Tính góc về phía địch gần nhất
float dx = enemy.x - myTank.x;
float dz = enemy.z - myTank.z;
float targetYaw = std::atan2(dx, dz);   // radians — game dùng rad, không phải degree

WriteProcessMemory(hProc, (LPVOID)myYawAddr, &targetYaw, sizeof(float), nullptr);
// Game sẽ đọc giá trị này khi build C2S_MOVE packet cho tick tiếp theo
```

#### Tại sao không ghi HP địch?

```
Server gửi snapshot 20 lần/giây.
Cheat ghi HP_địch = 0 vào RAM.
50ms sau: server gửi snapshot mới → game ghi đè lại HP = 100.
```

HP của tank địch luôn bị server reset. Ghi HP chỉ gây nhầm lẫn cho chính mình, không ảnh hưởng game server.

#### Tóm tắt hai hướng tấn công

| Hướng | Kỹ thuật | Tác dụng thực |
|-------|----------|---------------|
| Đọc RAM | `ReadProcessMemory` trên snapshot buffer | ESP: thấy vị trí địch |
| Ghi RAM | `WriteProcessMemory` vào `yaw` của tank mình | Aimbot: tank tự xoay |
| Ghi RAM (HP địch) | `WriteProcessMemory` vào health | Không hiệu quả — server reset mỗi tick |
| Ghi packet | Sửa C2S_MOVE trước khi gửi | Cần MITM/hook Winsock |

---

## Tổng kết — Con đường từ 0 đến hack position

```
Chưa biết gì
    │
    ▼
[Bước 1] Network recon: netstat → UDP :8080, bắt Wireshark
    │     → Server gửi packet lớn ~20 lần/giây
    ▼
[Bước 2] Đọc raw hex → thấy giá trị 2000 tại offset +4
    │     → Đây là opcode S2C_SNAPSHOT
    ▼
[Bước 3] Đọc TankProtocol.cs → có struct TankState 26 bytes
    │     → Server gửi vị trí TẤT CẢ người chơi
    ▼
[Bước 4] Tìm dữ liệu trong RAM: pattern scan tìm opcode 2000
    │     → Lọc theo bounds hợp lệ, lấy tick cao nhất
    ▼
[Bước 5] Fix offset: header 14 bytes + body prefix 2 bytes
    │     → TankState tại offset 16, không phải 12 hay 14
    ▼
[Bước 6] Parse TankState → x, z, hp, flags, yawAddr
    │     → Có vị trí tất cả người chơi
    ▼
[Bước 7] Render overlay: GDI + WS_EX_LAYERED + chroma key
    │     → Hiển thị vị trí địch lên màn hình game
    ▼
[Bước 8] Aimbot: atan2(dx, dz) → WriteProcessMemory(yawAddr)
         → Tank tự xoay về hướng địch gần nhất
```

Toàn bộ quá trình chỉ dùng **user-mode API**, không cần driver, không cần reverse engineering assembly — game tự cung cấp source code (`TankProtocol.cs`) và tự gửi toàn bộ data qua mạng rõ ràng.
