# Hướng dẫn Demo AntiCheat Kernel-Mode

**SE315.Q21 — Nhóm 4**

---

## Mục lục

1. [Tổng quan demo](#1-tổng-quan-demo)
2. [Yêu cầu hệ thống](#2-yêu-cầu-hệ-thống)
3. [Cài đặt một lần](#3-cài-đặt-một-lần)
4. [Chạy demo](#4-chạy-demo)
5. [Những gì bạn sẽ thấy](#5-những-gì-bạn-sẽ-thấy)
6. [Xem log real-time](#6-xem-log-real-time)
7. [Dừng demo](#7-dừng-demo)
8. [Xử lý sự cố](#8-xử-lý-sự-cố)

---

## 1. Tổng quan demo

Demo minh hoạ hai kịch bản đối lập:

| Kịch bản | Kết quả |
|---|---|
| **Cheat tool không có AntiCheat** | Đọc được memory game, hiện vị trí + HP tất cả người chơi trên overlay |
| **Cheat tool có AntiCheat kernel-mode** | Không đọc được gì, overlay trắng, log ghi lại mọi lần bị chặn |

**Cơ chế hoạt động:**
- Driver chạy ở Ring 0 (kernel), đăng ký callback `ObRegisterCallbacks`
- Mỗi lần cheat gọi `OpenProcess(PROCESS_VM_READ, gameProcess)`, kernel callback chặn và strip quyền `VM_READ` trước khi trả handle về cho cheat
- Cheat nhận handle nhưng không có quyền đọc → `ReadProcessMemory` trả về `ERROR_ACCESS_DENIED`

---

## 2. Yêu cầu hệ thống

- Windows 10/11 64-bit
- WSL2 (Ubuntu) đã cài
- Visual Studio 2022 Community đã cài
- Quyền Administrator
- **Đã reboot** sau khi cài đặt (để test-signing có hiệu lực)

---

## 3. Cài đặt một lần

> Chỉ cần làm **một lần duy nhất**. Sau khi xong không cần làm lại.

### Bước 1 — Build và cài driver

Mở WSL terminal, chạy:

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km/run_km.sh
```

Script tự động:
- Tìm WDK headers/libs
- Build `anticheat_km.sys` + `loader.exe`
- Bật test-signing (`bcdedit /set testsigning on`)
- Đăng ký Scheduled Task tự load driver sau mỗi lần reboot

Khi script hỏi **"Reboot now? (y/n)"** → gõ `y`

### Bước 2 — Reboot Windows

```
Reboot ngay → login lại → driver tự load qua Scheduled Task
```

### Bước 3 — Verify (tuỳ chọn)

Sau khi login lại, kiểm tra driver đang chạy:

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km/run_km.sh status
# Output: Driver status: RUNNING
```

---

## 4. Chạy demo

### Demo đầy đủ 2 giai đoạn (khuyến nghị)

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/demo.sh
```

Script sẽ dẫn qua 2 giai đoạn với prompt xác nhận giữa các bước.

---

### Giai đoạn 1: Cheat hoạt động (không có AntiCheat)

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/demo.sh nocheat
```

**Việc script làm:**
1. Tắt AntiCheat driver (nếu đang chạy)
2. Khởi động backend (nếu chưa chạy)
3. Chạy `tank_hp_hack.exe`

**Kết quả quan sát được:**
- Cửa sổ cheat hiện ESP overlay với vị trí + HP tất cả người chơi
- Console in ra thông tin match theo thời gian thực

---

### Giai đoạn 2: Cheat bị chặn (có AntiCheat)

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/demo.sh ac
```

**Việc script làm:**
1. Kill cheat tool cũ
2. Load AntiCheat driver (UAC popup → bấm **Yes**)
3. Chạy lại `tank_hp_hack.exe`

**Kết quả quan sát được:**
- Cửa sổ cheat chỉ hiện `"Chua vao match..."` mãi mãi
- DebugView ghi log mỗi lần bị chặn

---

## 5. Những gì bạn sẽ thấy

### Cheat tool — Không có AntiCheat

```
=== TankOnline ESP v1.0 ===
Match:716594  Tick:61459  Bot:OFF

[ME ] ID:1  HP:100  (12.3, 0.0, 45.6)
[ENE] ID:2  HP: 85  (98.1, 0.0, 12.4)  << đọc được vị trí địch
[ENE] ID:3  HP: 60  (34.2, 0.0, 78.9)  << đọc được vị trí địch
```

Và overlay GDI trên màn hình hiện minimap với vị trí tất cả xe.

---

### Cheat tool — Có AntiCheat

```
=== TankOnline ESP v1.0 ===
Chua vao match...
```

Overlay trắng. Không có thông tin nào. Cheat bị mù hoàn toàn.

---

### DebugView — Khi AntiCheat chặn

Mở **DebugView** (Sysinternals), bật **Capture Kernel** (`Ctrl+K`), filter `[AC-KM]`:

```
[AC-KM] AntiCheat driver loaded. Monitoring: "Tank Legends.exe"
[AC-KM] Game started: PID=4832  EPROCESS=FFFF8A0123456780
[AC-KM] BLOCKED memory access from PID=7120  desired=0x001FFFFF  stripped=0x001FF1FB
[AC-KM] BLOCKED memory access from PID=7120  desired=0x001FFFFF  stripped=0x001FF1FB
```

Mỗi dòng `BLOCKED` = 1 lần cheat cố đọc memory game bị chặn.

- `PID=7120` — PID của tiến trình cheat
- `desired=0x001FFFFF` — Quyền cheat yêu cầu (bao gồm PROCESS_VM_READ)
- `stripped=0x001FF1FB` — Quyền sau khi strip (không còn VM_READ/WRITE/OPERATION)

---

## 6. Xem log real-time

### Cách 1 — DebugView (Sysinternals)

1. Download [DebugView](https://learn.microsoft.com/en-us/sysinternals/downloads/debugview) nếu chưa có
2. Chạy với quyền Administrator
3. Menu **Capture** → **Capture Kernel** (hoặc `Ctrl+K`)
4. Menu **Edit** → **Filter/Highlight** → nhập `[AC-KM]`

### Cách 2 — WinDbg (nâng cao)

Kết nối kernel debugger và theo dõi `DbgPrint` output.

---

## 7. Dừng demo

```bash
# Dừng cheat tool và unload driver
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/demo.sh stop

# Chỉ unload driver (giữ cheat tool)
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km/run_km.sh unload
```

---

## 8. Xử lý sự cố

### Driver không load sau reboot

```bash
# Load thủ công
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km/run_km.sh
```

Nếu báo lỗi về test-signing → reboot lại một lần nữa.

---

### Cheat vẫn hoạt động dù driver đã load

**Nguyên nhân:** Cheat đang giữ handle cũ (mở trước khi driver load).

**Fix:** Script `demo.sh` đã xử lý — nó kill cheat trước khi load driver, rồi mới khởi động lại cheat. Nếu chạy thủ công, làm theo thứ tự:
1. Kill cheat
2. Load driver
3. Mở cheat lại

---

### `demo.sh` báo "Driver chưa build"

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km/run_km.sh
```

---

### Backend không start

```bash
cd /home/minhk/project/SE315.Q21
bash start.sh
```

---

## Cấu trúc file liên quan

```
tools/
├── demo.sh                    ← Script demo chính (chạy cái này)
├── tank_hp_hack.exe           ← Cheat tool (target bị chặn)
├── run_cheat.sh               ← Build + chạy cheat tool thủ công
└── anticheat_km/
    ├── run_km.sh              ← Cài đặt driver (chạy 1 lần)
    ├── anticheat_km.sys       ← Kernel driver (sau khi build)
    ├── loader/loader.exe      ← Load/unload driver
    └── README.md              ← Tài liệu kỹ thuật chi tiết
```

---

*SE315.Q21 — Nhóm 4 — AntiCheat Demo Guide*
