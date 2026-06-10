# AntiCheat User-Mode — SE315.Q21

## Tổng quan

Module phát hiện cheat cho game **TankOnline** chạy ở **user mode (Ring 3)**. Quét định kỳ mỗi 2 giây để tìm process đang cố đọc memory của game thông qua việc kiểm tra bảng handle toàn hệ thống.

---

## Cơ chế phát hiện — VM Read Handle Detection

### Vấn đề cần giải quyết

Để đọc được vị trí và HP của người chơi khác, cheat tool **bắt buộc** phải:

```cpp
// 1. Xin quyền đọc memory game
HANDLE h = OpenProcess(PROCESS_ALL_ACCESS, FALSE, game_pid);

// 2. Đọc dữ liệu từ memory game
ReadProcessMemory(h, address, buffer, size, &bytesRead);
```

Bước 1 tạo ra một **handle** lưu trong bảng handle của Windows với quyền `PROCESS_VM_READ`. Đây là dấu vết không thể che giấu.

---

### Luồng phát hiện

```
anticheat.exe (Admin)
        │
        ▼
NtQuerySystemInformation(SystemHandleInformation)
        │
        └─► Lấy TOÀN BỘ handle của mọi process trong hệ thống
                │
                ▼
        Duyệt từng handle:
        ┌─────────────────────────────────────────────┐
        │ Bỏ qua: handle của game và anticheat        │
        │ Bỏ qua: handle không có quyền VM_READ       │
        │ Bỏ qua: process trong whitelist             │
        └─────────────────────────────────────────────┘
                │
                ▼
        DuplicateHandle() → GetProcessId()
        Kiểm tra handle này có trỏ tới game PID không?
                │
                ├── Không → bỏ qua
                │
                └── Có → CHEAT DETECTED
```

---

### Chi tiết kỹ thuật

**API chính:** `NtQuerySystemInformation` (undocumented, từ `ntdll.dll`)

```cpp
typedef NTSTATUS(NTAPI* PFN_NtQSI)(ULONG, PVOID, ULONG, PULONG);
NtQSI = GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQuerySystemInformation");
NtQSI(SystemHandleInformation /*16*/, buffer, bufSize, &returnLen);
```

Trả về bảng `SYSTEM_HANDLE_TABLE` chứa tất cả handle đang mở trong hệ thống:

```cpp
struct SYSTEM_HANDLE_ENTRY {
    ULONG       ProcessId;     // Process đang giữ handle
    USHORT      Handle;        // Giá trị handle
    PVOID       Object;        // Con trỏ kernel object
    ACCESS_MASK GrantedAccess; // Quyền được cấp
};
```

**Xác nhận handle trỏ tới game:**

```cpp
// Mở process đang giữ handle đáng ngờ
HANDLE hProc = OpenProcess(PROCESS_DUP_HANDLE, FALSE, suspectPid);

// Duplicate handle sang anticheat process để đọc thông tin
DuplicateHandle(hProc, suspectHandle, GetCurrentProcess(), &hDup,
                PROCESS_QUERY_LIMITED_INFORMATION, FALSE, 0);

// Lấy PID mà handle này trỏ tới
DWORD targetPid = GetProcessId(hDup);

// Nếu trỏ tới game → cheat bị phát hiện
if (targetPid == gamePid) → DETECTED
```

**Access mask bị coi là nguy hiểm:**

```cpp
PROCESS_VM_READ      = 0x0010  // Đọc memory
PROCESS_ALL_ACCESS   = 0x1FFFFF // Toàn quyền
PROCESS_VM_OPERATION = 0x0008  // Thao tác memory
```

`tank_hp_hack.exe` dùng `PROCESS_ALL_ACCESS (0x001FFFFF)` → bị bắt ngay.

---

## Whitelist — Process hợp lệ

Một số process hệ thống cũng cần đọc memory game (crash handler, debugger hệ thống) nhưng không phải cheat. Các process này được bỏ qua:

| Process | Lý do |
|---------|-------|
| `UnityCrashHandler64.exe` | Unity crash monitor — theo dõi game để ghi log khi crash |
| `UnityCrashHandler32.exe` | Như trên, phiên bản 32-bit |
| `werfault.exe` | Windows Error Reporting |
| `taskmgr.exe` | Task Manager |
| `MsMpEng.exe` | Windows Defender |

---

## Output

**Khi không có cheat** (cập nhật mỗi 2s, ghi đè cùng dòng `\r`):
```
[23:52:24] [OK] No cheat detected  (game PID=16148)
```

**Khi phát hiện cheat:**
```
[23:53:19] [!!] CHEAT DETECTED
       PID      : 21820
       Process  : tank_hp_hack.exe
       Reason   : VM read handle, access=0x001FFFFF
       Type     : Memory read handle
```

| Trường | Ý nghĩa |
|--------|---------|
| `PID` | Process ID của cheat tool |
| `Process` | Tên file thực thi của cheat |
| `Reason` | Lý do bị phát hiện + access mask |
| `Type` | `Memory read handle` = phát hiện qua handle scan |

---

## Yêu cầu và giới hạn

**Yêu cầu:**
- Chạy với quyền **Administrator** (cần `DuplicateHandle`)
- Game `Tank Legends.exe` phải đang chạy

**Giới hạn:**
- Chỉ phát hiện, **không chặn** — cheat vẫn đọc được memory
- Cheat có thể qua mặt bằng cách inject vào process hợp lệ (whitelist bypass)
- Không phát hiện cheat dùng kernel driver (đọc memory từ Ring 0)

---

## Khởi động

```bash
# Từ WSL2
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat/run_anticheat.sh

# Hoặc trực tiếp trên Windows (PowerShell Admin)
D:\Unity\TankOnline\SE315.Q21\tools\anticheat\anticheat.exe
```

## Build

```bash
bash /mnt/d/Unity/TankOnline/SE315.Q21/tools/anticheat/run_anticheat.sh
```

Lệnh build thủ công:
```
cl /EHsc /W3 /std:c++17 /nologo anticheat.cpp /Fe:anticheat.exe /link ntdll.lib
```

---

## So sánh với Kernel-Mode AntiCheat

| | User-Mode (`anticheat/`) | Kernel-Mode (`anticheat_km/`) |
|---|---|---|
| **Ring** | Ring 3 | Ring 0 |
| **Làm gì** | Phát hiện | Chặn |
| **API** | `NtQuerySystemInformation` | `ObRegisterCallbacks` |
| **Khi cheat chạy** | Log `[!!] CHEAT DETECTED` | Cheat nhận `ERROR_ACCESS_DENIED` |
| **Cần** | Admin | Admin + test-signing + reboot |
| **Qua mặt được không** | Có (inject vào whitelist process) | Không (chặn ở kernel trước khi handle tạo ra) |

---

*SE315.Q21 — Nhóm 4*
