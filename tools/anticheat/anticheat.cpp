/*
 * anticheat.cpp  —  SE315.Q21  AntiCheat Demo
 *
 * Phat hien:
 *   [1] Process nao dang giu handle toi game voi quyen doc bo nho (PROCESS_VM_READ)
 *   [2] Ten process khop danh sach cheat da biet
 *
 * Build: cl /EHsc /W3 /std:c++17 /nologo anticheat.cpp /Fe:anticheat.exe /link ntdll.lib
 */

#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>
#include <winternl.h>
#include <vector>
#include <string>
#include <iostream>
#include <iomanip>
#include <chrono>
#include <thread>
#include <ctime>
#include <algorithm>

// ── NtQuerySystemInformation ──────────────────────────────────────────────────
typedef NTSTATUS(NTAPI* PFN_NtQSI)(ULONG, PVOID, ULONG, PULONG);
static PFN_NtQSI NtQSI = nullptr;

#define SystemHandleInformation 16
struct SYSTEM_HANDLE_ENTRY {
    ULONG       ProcessId;
    UCHAR       ObjectTypeNumber;
    UCHAR       Flags;
    USHORT      Handle;
    PVOID       Object;
    ACCESS_MASK GrantedAccess;
};
struct SYSTEM_HANDLE_TABLE {
    ULONG Count;
    SYSTEM_HANDLE_ENTRY Handles[1];
};

// ── Danh sach cheat process (lowercase) ──────────────────────────────────────
static const std::vector<std::wstring> CHEAT_NAMES = {
    L"tank_hp_hack.exe",
    L"tank_aimbot.exe",
    L"cheatengine-x86_64.exe",
    L"cheatengine-x86_64-SSE4-AVX2.exe",
    L"cheatengine.exe",
    L"artmoney.exe",
    L"tsearch.exe",
    L"pkhex.exe",
    L"processhacker.exe",
    L"process hacker.exe",
    L"x64dbg.exe",
    L"x32dbg.exe",
    L"ollydbg.exe",
    L"ida.exe",
    L"ida64.exe",
    L"scylla.exe",
};

// Process hop le duoc phep giu handle toi game (whitelist)
static const std::vector<std::wstring> WHITELIST = {
    L"unityCrashHandler64.exe",  // Unity crash monitor
    L"unityCrashHandler32.exe",
    L"werfault.exe",             // Windows Error Reporting
    L"taskmgr.exe",              // Task Manager
    L"msMpEng.exe",              // Windows Defender
};

// Access mask flags lien quan den doc bo nho
static const ACCESS_MASK MEM_READ_FLAGS =
    PROCESS_VM_READ | PROCESS_ALL_ACCESS | PROCESS_VM_OPERATION;

// ── Helpers ───────────────────────────────────────────────────────────────────
static std::wstring toLower(std::wstring s) {
    std::transform(s.begin(), s.end(), s.begin(), ::towlower);
    return s;
}
static bool isWhitelisted(const std::wstring& name) {
    std::wstring lower = toLower(name);
    for (auto& w : WHITELIST)
        if (lower == toLower(w)) return true;
    return false;
}
static std::string timestamp() {
    auto now = std::chrono::system_clock::now();
    std::time_t t = std::chrono::system_clock::to_time_t(now);
    char buf[32]; std::strftime(buf, sizeof(buf), "%H:%M:%S", std::localtime(&t));
    return buf;
}
static std::wstring getProcessName(DWORD pid) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return L"";
    PROCESSENTRY32W e = {sizeof(e)};
    std::wstring name;
    if (Process32FirstW(snap, &e))
        do { if (e.th32ProcessID == pid) { name = e.szExeFile; break; } }
        while (Process32NextW(snap, &e));
    CloseHandle(snap);
    return name;
}
static DWORD findGamePID(const wchar_t* procName) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W e = {sizeof(e)};
    DWORD pid = 0;
    if (Process32FirstW(snap, &e))
        do { if (_wcsicmp(e.szExeFile, procName) == 0) { pid = e.th32ProcessID; break; } }
        while (Process32NextW(snap, &e));
    CloseHandle(snap);
    return pid;
}

// ── Detection result ──────────────────────────────────────────────────────────
struct Detection {
    enum Type { HANDLE_SCAN, KNOWN_CHEAT } type;
    DWORD        pid;
    std::wstring procName;
    ACCESS_MASK  access;   // only for HANDLE_SCAN
    std::string  reason;
};

// ── [1] Handle scan: tim process giu handle toi game voi quyen VM_READ ────────
static std::vector<Detection> scanHandles(DWORD gamePid) {
    std::vector<Detection> found;
    if (!NtQSI) return found;

    // Doc bang handle toan he thong
    ULONG bufSize = 1 << 20;  // 1MB ban dau
    std::vector<BYTE> buf;
    NTSTATUS status;
    do {
        buf.resize(bufSize);
        ULONG ret = 0;
        status = NtQSI(SystemHandleInformation, buf.data(), bufSize, &ret);
        if (status == 0xC0000004L /*STATUS_INFO_LENGTH_MISMATCH*/) bufSize *= 2;
    } while (status == 0xC0000004L && bufSize < 256u*1024*1024);

    if (status != 0) return found;

    auto* tbl = reinterpret_cast<SYSTEM_HANDLE_TABLE*>(buf.data());
    DWORD myPid = GetCurrentProcessId();

    for (ULONG i = 0; i < tbl->Count; i++) {
        auto& h = tbl->Handles[i];
        // Bo qua handle cua chinh game va anticheat
        if (h.ProcessId == gamePid || h.ProcessId == myPid) continue;
        // Chi quan tam handle co quyen doc bo nho
        if (!(h.GrantedAccess & MEM_READ_FLAGS)) continue;

        // Kiem tra handle nay co tro toi game process khong
        // Bang cach: mo process chua handle, duplicate handle, kiem tra
        HANDLE hProc = OpenProcess(PROCESS_DUP_HANDLE, FALSE, h.ProcessId);
        if (!hProc) continue;

        HANDLE hDup = nullptr;
        if (!DuplicateHandle(hProc, (HANDLE)(uintptr_t)h.Handle,
                             GetCurrentProcess(), &hDup,
                             PROCESS_QUERY_LIMITED_INFORMATION, FALSE, 0)) {
            CloseHandle(hProc); continue;
        }

        // Lay PID cua process ma handle nay tro toi
        DWORD targetPid = GetProcessId(hDup);
        CloseHandle(hDup); CloseHandle(hProc);

        if (targetPid != gamePid) continue;

        // Xac nhan: day la handle tro toi game voi quyen doc bo nho
        std::wstring name = getProcessName(h.ProcessId);
        if (isWhitelisted(name)) continue;  // bo qua process hop le
        char acc[64]; sprintf_s(acc, "0x%08X", h.GrantedAccess);
        found.push_back({Detection::HANDLE_SCAN, h.ProcessId, name,
                         h.GrantedAccess,
                         std::string("VM read handle, access=") + acc});
    }
    return found;
}

// ── [2] Known cheat process scan ─────────────────────────────────────────────
static std::vector<Detection> scanKnownCheats() {
    std::vector<Detection> found;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return found;
    PROCESSENTRY32W e = {sizeof(e)};
    if (Process32FirstW(snap, &e)) {
        do {
            std::wstring lower = toLower(e.szExeFile);
            for (auto& cheat : CHEAT_NAMES) {
                if (lower == toLower(cheat)) {
                    found.push_back({Detection::KNOWN_CHEAT, e.th32ProcessID,
                                     e.szExeFile, 0, "Matches known cheat name"});
                    break;
                }
            }
        } while (Process32NextW(snap, &e));
    }
    CloseHandle(snap);
    return found;
}

// ── Report ────────────────────────────────────────────────────────────────────
static void report(const std::vector<Detection>& list, DWORD gamePid) {
    if (list.empty()) {
        std::cout << "[" << timestamp() << "] [OK] No cheat detected  "
                  << "(game PID=" << gamePid << ")\r" << std::flush;
        return;
    }
    std::cout << "\n";
    for (auto& d : list) {
        std::cout << "[" << timestamp() << "] [!!] CHEAT DETECTED\n"
                  << "       PID      : " << d.pid << "\n"
                  << "       Process  : ";
        std::wcout << d.procName << L"\n";
        std::cout << "       Reason   : " << d.reason << "\n"
                  << "       Type     : "
                  << (d.type == Detection::HANDLE_SCAN ? "Memory read handle" : "Known cheat process")
                  << "\n\n";
    }
}

// ── Main ──────────────────────────────────────────────────────────────────────
int main() {
    // Load NtQuerySystemInformation
    NtQSI = (PFN_NtQSI)GetProcAddress(GetModuleHandleW(L"ntdll.dll"),
                                       "NtQuerySystemInformation");
    if (!NtQSI) {
        std::cerr << "[-] Cannot load NtQuerySystemInformation\n";
        return 1;
    }

    std::cout << "=== TankOnline AntiCheat — SE315.Q21 ===\n";
    std::cout << "Scan interval: 2s\n";
    std::cout << "Method: VM-read handle detection (NtQuerySystemInformation)\n\n";

    const wchar_t* GAME = L"Tank Legends.exe";
    DWORD gamePid = 0;

    while (true) {
        DWORD newPid = findGamePID(GAME);
        if (newPid != gamePid) {
            gamePid = newPid;
            if (gamePid)
                std::cout << "[" << timestamp() << "] Game found: PID=" << gamePid << "\n";
            else
                std::cout << "[" << timestamp() << "] Game not running. Waiting...\n";
        }

        if (gamePid) {
            std::vector<Detection> all;

            // [1] Handle scan (can quyen Admin de DuplicateHandle)
            auto h = scanHandles(gamePid);
            all.insert(all.end(), h.begin(), h.end());

            // [2] Known cheat names (khong can quyen dac biet)
            // auto k = scanKnownCheats();
            // all.insert(all.end(), k.begin(), k.end());

            // Loai bung tung trung (cung PID)
            std::vector<Detection> deduped;
            for (auto& d : all) {
                bool dup = false;
                for (auto& e : deduped) if (e.pid == d.pid) { dup=true; break; }
                if (!dup) deduped.push_back(d);
            }

            report(deduped, gamePid);
        }

        std::this_thread::sleep_for(std::chrono::seconds(2));
    }
}
