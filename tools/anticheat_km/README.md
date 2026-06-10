# TankOnline AntiCheat — Kernel-Mode Driver

**SE315.Q21 — Nhóm 4**

---

## Mục lục

1. [Tổng quan](#1-tổng-quan)
2. [Tại sao cần kernel mode](#2-tại-sao-cần-kernel-mode)
3. [Kiến trúc hệ thống](#3-kiến-trúc-hệ-thống)
4. [Cơ chế hoạt động chi tiết](#4-cơ-chế-hoạt-động-chi-tiết)
5. [Cấu trúc thư mục](#5-cấu-trúc-thư-mục)
6. [Hướng dẫn cài đặt và chạy](#6-hướng-dẫn-cài-đặt-và-chạy)
7. [Demo: Cheat bị chặn](#7-demo-cheat-bị-chặn)
8. [Giới hạn và hướng mở rộng](#8-giới-hạn-và-hướng-mở-rộng)

---

## 1. Tổng quan

AntiCheat này là thành phần bảo vệ phía client cho game **Tank Legends**. Nó chạy ở **kernel mode** (Ring 0) — tầng đặc quyền cao nhất của hệ điều hành Windows — để ngăn chặn các công cụ cheat đọc bộ nhớ game trước khi chúng nhận được quyền truy cập.

### Vấn đề cần giải quyết

Tool cheat `tank_hp_hack.exe` hoạt động bằng cách:
1. Gọi `OpenProcess(PROCESS_VM_READ, ...)` để lấy handle tới tiến trình game
2. Dùng `ReadProcessMemory()` để đọc snapshot UDP (`S2C_SNAPSHOT`, opcode 2000)
3. Hiển thị vị trí, HP của tất cả người chơi lên overlay

AntiCheat kernel-mode ngắt chuỗi này ngay tại **bước 1** — trước khi `OpenProcess` trả về handle.

---

## 2. Tại sao cần kernel mode

### User-mode anticheat (không đủ)

```
[Cheat process]  →  OpenProcess(PROCESS_VM_READ)  →  Kernel cấp handle
                                                            ↓
[AntiCheat UM]  ←──────────────── Enumerate handles (quá muộn) ──────────
```

- Anticheat user-mode chỉ **phát hiện sau khi** handle đã được cấp
- Kẻ tấn công có thể chạy cheat với quyền SYSTEM, vượt qua kiểm tra tên process
- Handle đã cấp → `ReadProcessMemory` đã có thể chạy → dữ liệu bị lộ

### Kernel-mode anticheat (đúng cách)

```
[Cheat process]  →  OpenProcess(PROCESS_VM_READ)
                              ↓
                    [Kernel: ObRegisterCallbacks]
                              ↓
                    Driver nhận callback TRƯỚC KHI handle cấp
                              ↓
                    Strip PROCESS_VM_READ khỏi DesiredAccess
                              ↓
                    Cheat nhận handle KHÔNG có quyền đọc bộ nhớ
```

- Hoạt động tại **Ring 0**, không thể bị bypass bởi code user-mode
- Chặn tại syscall, trước khi bất kỳ quyền nào được trao
- Không phụ thuộc tên process hay PID của cheat

---

## 3. Kiến trúc hệ thống

```
┌─────────────────────────────────────────────────────────┐
│                    User Mode (Ring 3)                    │
│                                                          │
│  ┌─────────────────┐        ┌──────────────────────┐    │
│  │  Tank Legends   │        │   tank_hp_hack.exe   │    │
│  │  (game)         │        │   (cheat tool)       │    │
│  └────────┬────────┘        └──────────┬───────────┘    │
│           │                            │                 │
│           │                   OpenProcess(VM_READ)       │
│           │                            │                 │
├───────────┼────────────────────────────┼─────────────────┤
│           │        Kernel (Ring 0)     │                 │
│           │                            ▼                 │
│           │               ┌────────────────────────┐    │
│           │               │  ObRegisterCallbacks   │    │
│           │               │  Pre-callback          │    │
│           │               │  → Strip VM_READ       │    │
│           │               │  → Log detection       │    │
│           │               └────────────────────────┘    │
│           │                                              │
│  ┌────────▼────────────────────────────────────────┐    │
│  │  PsSetCreateProcessNotifyRoutineEx               │    │
│  │  → Theo dõi khi game start/exit                 │    │
│  │  → Lưu EPROCESS pointer của game                │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

### Các thành phần

| Thành phần | File | Vai trò |
|---|---|---|
| Kernel Driver | `anticheat_km.sys` | Chạy Ring 0, đăng ký callbacks |
| User Loader | `loader/loader.exe` | Cài/khởi động/gỡ driver |
| Setup Script | `setup_anticheat.ps1` | Tự động hoá toàn bộ quá trình |

---

## 4. Cơ chế hoạt động chi tiết

### 4.1 Theo dõi game process — `PsSetCreateProcessNotifyRoutineEx`

Khi driver load, nó đăng ký một callback được kernel gọi mỗi khi **bất kỳ process nào** được tạo hoặc tắt trên toàn hệ thống.

```c
PsSetCreateProcessNotifyRoutineEx(AcProcessNotify, FALSE);

void AcProcessNotify(PEPROCESS Process, HANDLE ProcessId,
                     PPS_CREATE_NOTIFY_INFO CreateInfo) {
    if (CreateInfo) {
        // Process đang tạo — kiểm tra tên image
        if (IsGameProcess(CreateInfo)) {
            g_gameProcess = Process;   // lưu EPROCESS pointer
            g_gamePid     = ProcessId;
        }
    } else {
        // Process đang tắt
        if (ProcessId == g_gamePid) {
            g_gameProcess = NULL;
        }
    }
}
```

**Kết quả:** Driver luôn biết EPROCESS của game, kể cả khi game khởi động sau driver.

---

### 4.2 Chặn handle — `ObRegisterCallbacks`

Đây là cơ chế cốt lõi. Kernel gọi callback này **trước khi** trả về handle cho caller.

```c
OB_PREOP_CALLBACK_STATUS AcObPreCallback(
    PVOID RegistrationContext,
    POB_PRE_OPERATION_INFORMATION Info)
{
    PEPROCESS target = (PEPROCESS)Info->Object;

    // Chỉ quan tâm handle tới game process
    if (target != g_gameProcess) return OB_PREOP_SUCCESS;

    // Game tự mở chính nó → cho phép
    if (PsGetCurrentProcess() == g_gameProcess) return OB_PREOP_SUCCESS;

    ACCESS_MASK desired = Info->Parameters->CreateHandleInformation.DesiredAccess;

    // Nếu yêu cầu quyền đọc/ghi bộ nhớ → strip
    if (desired & (PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION)) {
        Info->Parameters->CreateHandleInformation.DesiredAccess &= ~STRIP_ACCESS;
        // Log: PID của cheat, access mask bị strip
    }
    return OB_PREOP_SUCCESS;
}
```

**Kết quả:** Cheat nhận được handle nhưng không có quyền `PROCESS_VM_READ`. Mọi lời gọi `ReadProcessMemory` sau đó đều trả về `ERROR_ACCESS_DENIED`.

---

### 4.3 Luồng xử lý khi cheat chạy

```
tank_hp_hack.exe khởi động
        │
        ▼
OpenProcess(PROCESS_ALL_ACCESS, gamePid)
        │
        ▼  ← Kernel intercepts tại đây
AcObPreCallback() được gọi
        │
        ├─ target == g_gameProcess? YES
        ├─ caller == game? NO
        ├─ desired has VM_READ? YES
        │
        ▼
Strip PROCESS_VM_READ | PROCESS_VM_OPERATION | PROCESS_VM_WRITE
Log: "[AC-KM] BLOCKED PID=1234 desired=0x001FFFFF stripped=0x001FF1FB"
        │
        ▼
Trả về handle với access = 0x001FF1FB (không có VM_READ)
        │
        ▼
ReadProcessMemory(handle, ...) → ERROR_ACCESS_DENIED (5)
        │
        ▼
Cheat không đọc được bộ nhớ → không hiện ESP, không có vị trí
```

---

### 4.4 Loader — Cài/gỡ driver

Loader dùng **Service Control Manager (SCM)** của Windows để load `.sys` vào kernel:

```
loader.exe load
    │
    ├─ CreateService(SERVICE_KERNEL_DRIVER, ...)  ← đăng ký với SCM
    └─ StartService(...)                          ← kernel load .sys vào memory
                                                    → DriverEntry() được gọi
loader.exe unload
    │
    ├─ ControlService(SERVICE_CONTROL_STOP)       ← kernel gọi DriverUnload()
    └─ DeleteService(...)                         ← xóa đăng ký SCM
```

---

### 4.5 Setup tự động — `setup_anticheat.ps1`

```
Chạy setup_anticheat.ps1
        │
        ├─[1] WDK km libs có không?
        │       NO  → Download wdksetup.exe (~500MB) → Cài silent
        │       YES → tiếp tục
        │
        ├─[2] Test-signing bật chưa?
        │       NO  → bcdedit /set testsigning on
        │           → Tạo Scheduled Task (load driver sau reboot)
        │           → Hỏi reboot ngay không
        │       YES → tiếp tục
        │
        ├─[3] Build: cl /kernel + link ntoskrnl.lib
        │
        ├─[4] Sign: makecert → test cert → signtool sign .sys
        │
        └─[5] Load: loader.exe load → driver vào kernel
```

---

## 5. Cấu trúc thư mục

```
tools/anticheat_km/
├── anticheat_km.c        ← Kernel driver source (Ring 0)
│     ├── DriverEntry()         Đăng ký callbacks khi load
│     ├── AcProcessNotify()     Theo dõi game process
│     ├── AcObPreCallback()     Strip VM_READ khỏi handle
│     └── AcDriverUnload()      Dọn dẹp khi unload
│
├── loader/
│   └── loader.cpp        ← User-mode loader (load/unload/status)
│
├── setup_anticheat.ps1   ← Tự động hoá toàn bộ (WDK + build + sign + load)
├── build.bat             ← Build thủ công (cần WDK đã cài)
├── run_km.sh             ← Entry point từ WSL2
└── README.md             ← File này
```

---

## 6. Hướng dẫn cài đặt và chạy

### Yêu cầu

- Windows 10/11 (64-bit)
- Visual Studio 2022 Community (đã cài)
- Quyền Administrator
- WDK: **tự động tải** nếu chưa có

### Chạy (1 lệnh duy nhất từ WSL2)

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat_km/run_km.sh
```

Script sẽ tự làm hết. Nếu đây là lần đầu (test-signing chưa bật):

```
[!] REBOOT REQUIRED
Reboot ngay? (y/n): y
```

→ Sau khi reboot và login lại, driver tự động được load.

### Kiểm tra driver đang chạy

```bash
bash run_km.sh status
# → Driver status: RUNNING
```

### Xem log real-time

Mở **DebugView** (Sysinternals Suite) trên Windows:
- Filter: `[AC-KM]`
- Mỗi lần cheat mở handle tới game sẽ thấy:

```
[AC-KM] Game started: PID=4832  EPROCESS=FFFF...
[AC-KM] BLOCKED memory access from PID=7120  desired=0x001FFFFF  stripped=0x001FF1FB
```

### Gỡ driver

```bash
bash run_km.sh unload
```

---

## 7. Demo: Cheat bị chặn

### Không có AntiCheat

```
tank_hp_hack.exe chạy
→ OpenProcess thành công, nhận handle đầy đủ quyền
→ ReadProcessMemory đọc S2C_SNAPSHOT
→ Hiện vị trí và HP tất cả người chơi trên overlay
```

### Có AntiCheat kernel-mode

```
tank_hp_hack.exe chạy
→ OpenProcess bị driver intercept → handle trả về thiếu PROCESS_VM_READ
→ ReadProcessMemory → ERROR_ACCESS_DENIED
→ Overlay chỉ hiện "Chua vao match..." mãi mãi
→ Log trong DebugView: "[AC-KM] BLOCKED PID=7120 ..."
```

---

## 8. Giới hạn và hướng mở rộng

### Giới hạn hiện tại

| Giới hạn | Giải thích |
|---|---|
| Test-signing required | Driver cần chữ ký EV Certificate để deploy thực tế (≈$300/năm) |
| Chỉ chặn VM_READ | Không phát hiện DLL injection, packet manipulation |
| Không có server-side | AntiCheat client-side có thể bị bypass nếu attacker patch driver |
| Kernel panic risk | Driver lỗi có thể gây BSOD — cần test kỹ trên VM trước |

### Hướng mở rộng

- **Server-side validation**: Server kiểm tra vị trí di chuyển có hợp lệ không (speed hack detection)
- **PatchGuard**: Tích hợp với Windows PatchGuard để phát hiện patch kernel
- **Integrity check**: Hash toàn bộ bộ nhớ game định kỳ để phát hiện memory patch
- **HVCI (Hypervisor-Protected Code Integrity)**: Chạy driver trong môi trường hypervisor để tăng bảo mật

---

*SE315.Q21 — Nhóm 4 — AntiCheat Demo*
