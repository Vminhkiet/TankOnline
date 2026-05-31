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
#include <winhttp.h>
#include <vector>
#include <string>
#include <iostream>
#include <iomanip>
#include <chrono>
#include <thread>
#include <ctime>
#include <algorithm>
#include <sstream>
#pragma comment(lib, "winhttp.lib")

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
    L"werfaultsecure.exe",
    L"taskmgr.exe",              // Task Manager
    L"msMpEng.exe",              // Windows Defender
    L"explorer.exe",             // Windows Shell (taskbar, thumbnail, file manager)
    L"svchost.exe",              // Windows services host
    L"csrss.exe",                // Client Server Runtime
    L"lsass.exe",                // Local Security Authority
    L"dwm.exe",                  // Desktop Window Manager
    L"fontdrvhost.exe",          // Font driver host
    L"sihost.exe",               // Shell Infrastructure Host
    L"shellexperiencehost.exe",  // Windows Shell Experience
    L"searchindexer.exe",        // Windows Search
    L"smartscreen.exe",          // Windows SmartScreen
    L"gamebar.exe",              // Xbox Game Bar
    L"gamebarftserver.exe",
    L"nvcontainer.exe",          // NVIDIA container
    L"nvdisplay.container.exe",
    L"audiodg.exe",              // Audio Device Graph
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

// ── Ban via HTTP ──────────────────────────────────────────────────────────────
static const char* AC_SECRET  = "AC-SECRET-SE315";
static const char* AUTH_HOST  = "172.25.203.168";  // WSL2 IP — API Gateway
static const INTERNET_PORT AUTH_PORT = 8080;
static const wchar_t* BAN_PATH    = L"/api/user/anticheat/ban";
static const wchar_t* CANCEL_PATH = L"/api/matchmaking/admin/cancel-cheat";

// Base64 decode (JWT payload dung standard base64url: - → +, _ → /)
static std::string base64Decode(const std::string& in) {
    static const std::string chars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string s = in;
    for (auto& c : s) { if (c=='-') c='+'; else if (c=='_') c='/'; }
    while (s.size() % 4) s += '=';
    std::string out;
    int val=0, valb=-8;
    for (unsigned char c : s) {
        if (c == '=') break;
        auto p = chars.find(c);
        if (p == std::string::npos) continue;
        val = (val << 6) + (int)p; valb += 6;
        if (valb >= 0) { out += char((val >> valb) & 0xFF); valb -= 8; }
    }
    return out;
}

// Scan process memory tim JWT (eyJ...) va tra ve userId
static long long scanJwtUserId(HANDLE hProc) {
    MEMORY_BASIC_INFORMATION mbi; uintptr_t addr = 0;
    while (VirtualQueryEx(hProc, (LPCVOID)addr, &mbi, sizeof(mbi))) {
        addr += mbi.RegionSize;
        if (!(mbi.State == MEM_COMMIT && (mbi.Protect & PAGE_READWRITE))) continue;
        if (mbi.RegionSize > 64u*1024*1024) continue;
        std::vector<char> buf(mbi.RegionSize + 1, 0);
        SIZE_T n = 0;
        if (!ReadProcessMemory(hProc, mbi.BaseAddress, buf.data(), mbi.RegionSize, &n)) continue;
        // Tim chuoi JWT: bat dau bang "eyJ", co dang xxx.yyy.zzz
        for (SIZE_T i = 0; i + 10 < n; i++) {
            if (buf[i]!='e'||buf[i+1]!='y'||buf[i+2]!='J') continue;
            // Do dai hop le: 100-600 ky tu
            SIZE_T j = i;
            while (j < n && j < i+600 && (isalnum((unsigned char)buf[j]) || buf[j]=='-'||buf[j]=='_'||buf[j]=='.')) j++;
            if (j - i < 100) continue;
            std::string jwt(buf.data()+i, j-i);
            // Tach payload (phan thu 2 giua 2 dau cham)
            auto d1 = jwt.find('.');
            if (d1 == std::string::npos) continue;
            auto d2 = jwt.find('.', d1+1);
            if (d2 == std::string::npos) continue;
            std::string payload = base64Decode(jwt.substr(d1+1, d2-d1-1));
            // Tim "userId":"<number>"
            auto pos = payload.find("\"userId\"");
            if (pos == std::string::npos) continue;
            auto q1 = payload.find('"', pos+8);
            if (q1 == std::string::npos) continue;
            auto q2 = payload.find('"', q1+1);
            if (q2 == std::string::npos) continue;
            std::string idStr = payload.substr(q1+1, q2-q1-1);
            try { return std::stoll(idStr); } catch (...) {}
        }
    }
    return -1;
}

// Scan process memory tim matchId tu snapshot header (opcode 2000, offset +0 = matchId)
static uint32_t scanMatchId(HANDLE hProc) {
    MEMORY_BASIC_INFORMATION mbi; uintptr_t addr = 0;
    while (VirtualQueryEx(hProc, (LPCVOID)addr, &mbi, sizeof(mbi))) {
        addr += mbi.RegionSize;
        if (!(mbi.State == MEM_COMMIT && (mbi.Protect & PAGE_READWRITE))) continue;
        if (mbi.RegionSize > 64u*1024*1024) continue;
        std::vector<BYTE> buf(mbi.RegionSize); SIZE_T n = 0;
        if (!ReadProcessMemory(hProc, mbi.BaseAddress, buf.data(), mbi.RegionSize, &n)) continue;
        for (SIZE_T i = 4; i + 10 < n; i++) {
            uint16_t op; memcpy(&op, &buf[i], 2);
            if (op != 2000) continue;
            // bytes [i-4..i-1] = matchId (uint32)
            uint32_t matchId; memcpy(&matchId, &buf[i-4], 4);
            if (matchId == 0 || matchId > 1000000) continue;
            uint16_t tc; memcpy(&tc, &buf[i+4], 2);
            if (tc == 0 || tc > 8) continue;
            uint16_t localId; memcpy(&localId, &buf[i+6], 2);
            if (localId == 0 || localId > 255) continue;
            return matchId;
        }
    }
    return 0;
}

// Gui HTTP POST de cancel match
static bool sendHttpPost(const wchar_t* path, const std::string& bodyStr) {
    int wlen = MultiByteToWideChar(CP_ACP, 0, AUTH_HOST, -1, nullptr, 0);
    std::wstring wHost(wlen, 0);
    MultiByteToWideChar(CP_ACP, 0, AUTH_HOST, -1, &wHost[0], wlen);

    HINTERNET hSession = WinHttpOpen(L"AntiCheat/1.0",
        WINHTTP_ACCESS_TYPE_NO_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hSession) return false;
    HINTERNET hConnect = WinHttpConnect(hSession, wHost.c_str(), AUTH_PORT, 0);
    if (!hConnect) { WinHttpCloseHandle(hSession); return false; }
    HINTERNET hReq = WinHttpOpenRequest(hConnect, L"POST", path,
        nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, 0);
    if (!hReq) { WinHttpCloseHandle(hConnect); WinHttpCloseHandle(hSession); return false; }

    std::wstring wSecret(AC_SECRET, AC_SECRET + strlen(AC_SECRET));
    std::wstring headers = L"Content-Type: application/json\r\nX-Anticheat-Key: " + wSecret + L"\r\n";

    bool ok = WinHttpSendRequest(hReq, headers.c_str(), (DWORD)-1L,
        (LPVOID)bodyStr.c_str(), (DWORD)bodyStr.size(), (DWORD)bodyStr.size(), 0)
        && WinHttpReceiveResponse(hReq, nullptr);
    if (ok) {
        DWORD status = 0; DWORD sz = sizeof(status);
        WinHttpQueryHeaders(hReq, WINHTTP_QUERY_STATUS_CODE|WINHTTP_QUERY_FLAG_NUMBER,
            nullptr, &status, &sz, nullptr);
        ok = (status == 200);
        if (!ok) std::cout << "  HTTP " << path << " status=" << status << "\n";
    }
    WinHttpCloseHandle(hReq); WinHttpCloseHandle(hConnect); WinHttpCloseHandle(hSession);
    return ok;
}

// Gui HTTP POST ban request den auth service
static bool sendBanRequest(long long userId, const std::string& reason) {
    std::ostringstream body;
    body << "{\"userId\":" << userId << ",\"reason\":\"" << reason << "\"}";
    bool ok = sendHttpPost(BAN_PATH, body.str());
    if (!ok) std::cout << "  WinHttp err=" << GetLastError() << "\n";
    return ok;
}

static bool sendCancelRequest(uint32_t matchId, const std::string& reason) {
    std::ostringstream body;
    body << "{\"matchId\":" << matchId << ",\"reason\":\"" << reason << "\"}";
    return sendHttpPost(CANCEL_PATH, body.str());
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

            auto h = scanHandles(gamePid);
            all.insert(all.end(), h.begin(), h.end());

            auto k = scanKnownCheats();
            all.insert(all.end(), k.begin(), k.end());

            // Loai trung (cung PID)
            std::vector<Detection> deduped;
            for (auto& d : all) {
                bool dup = false;
                for (auto& e : deduped) if (e.pid == d.pid) { dup=true; break; }
                if (!dup) deduped.push_back(d);
            }

            report(deduped, gamePid);

            // Neu phat hien cheat → ban user + cancel match
            if (!deduped.empty()) {
                HANDLE hGame = OpenProcess(PROCESS_VM_READ|PROCESS_QUERY_INFORMATION, FALSE, gamePid);
                if (hGame) {
                    long long uid     = scanJwtUserId(hGame);
                    uint32_t matchId  = scanMatchId(hGame);
                    CloseHandle(hGame);

                    std::string reason = deduped[0].reason;

                    if (uid > 0) {
                        bool banned = sendBanRequest(uid, reason);
                        std::cout << "[" << timestamp() << "] "
                                  << (banned ? "[BAN] User banned" : "[BAN] Ban FAILED")
                                  << " → userId=" << uid << "\n";
                    } else {
                        std::cout << "[" << timestamp() << "] [BAN] Could not find userId in game memory\n";
                    }

                    if (matchId > 0) {
                        bool cancelled = sendCancelRequest(matchId, reason);
                        std::cout << "[" << timestamp() << "] "
                                  << (cancelled ? "[CANCEL] Match cancelled" : "[CANCEL] Cancel FAILED")
                                  << " → matchId=" << matchId << "\n";
                    } else {
                        std::cout << "[" << timestamp() << "] [CANCEL] Could not find matchId in game memory\n";
                    }
                }
            }
        }

        std::this_thread::sleep_for(std::chrono::seconds(2));
    }
}
