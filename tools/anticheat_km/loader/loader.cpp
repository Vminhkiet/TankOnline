/*
 * loader.cpp  —  SE315.Q21  AntiCheat KM Loader
 *
 * Load / unload driver anticheat_km.sys vao kernel.
 * Yeu cau: chay Administrator, Windows test-signing da bat
 *
 * Usage:
 *   loader.exe load    — cai va khoi dong driver
 *   loader.exe unload  — dung va go driver
 *   loader.exe status  — kiem tra driver dang chay chua
 */

#include <windows.h>
#include <iostream>
#include <string>

static const char* SVC_NAME = "TankAntiCheat";

static bool loadDriver(const std::string& sysPath) {
    SC_HANDLE scm = OpenSCManagerA(nullptr, nullptr, SC_MANAGER_CREATE_SERVICE);
    if (!scm) { std::cerr << "[-] OpenSCManager failed: " << GetLastError() << "\n"; return false; }

    // Xoa service cu neu ton tai
    SC_HANDLE svc = OpenServiceA(scm, SVC_NAME, SERVICE_ALL_ACCESS);
    if (svc) { ControlService(svc, SERVICE_CONTROL_STOP, nullptr); DeleteService(svc); CloseServiceHandle(svc); }

    svc = CreateServiceA(scm, SVC_NAME, SVC_NAME,
                         SERVICE_ALL_ACCESS, SERVICE_KERNEL_DRIVER,
                         SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                         sysPath.c_str(),
                         nullptr, nullptr, nullptr, nullptr, nullptr);
    CloseServiceHandle(scm);
    if (!svc) {
        std::cerr << "[-] CreateService failed: " << GetLastError() << "\n";
        return false;
    }

    BOOL ok = StartServiceA(svc, 0, nullptr);
    DWORD err = GetLastError();
    CloseServiceHandle(svc);

    if (!ok && err != ERROR_SERVICE_ALREADY_RUNNING) {
        std::cerr << "[-] StartService failed: " << err << "\n";
        if (err == 577)
            std::cerr << "    → Driver chua duoc ky so. Bat test-signing:\n"
                      << "      bcdedit /set testsigning on  (reboot)\n";
        return false;
    }
    std::cout << "[+] Driver loaded OK\n";
    return true;
}

static bool unloadDriver() {
    SC_HANDLE scm = OpenSCManagerA(nullptr, nullptr, SC_MANAGER_CONNECT);
    if (!scm) return false;
    SC_HANDLE svc = OpenServiceA(scm, SVC_NAME, SERVICE_STOP | DELETE | SERVICE_QUERY_STATUS);
    CloseServiceHandle(scm);
    if (!svc) { std::cerr << "[-] Service not found\n"; return false; }

    SERVICE_STATUS ss;
    ControlService(svc, SERVICE_CONTROL_STOP, &ss);
    Sleep(500);
    DeleteService(svc);
    CloseServiceHandle(svc);
    std::cout << "[+] Driver unloaded OK\n";
    return true;
}

static void statusDriver() {
    SC_HANDLE scm = OpenSCManagerA(nullptr, nullptr, SC_MANAGER_CONNECT);
    if (!scm) return;
    SC_HANDLE svc = OpenServiceA(scm, SVC_NAME, SERVICE_QUERY_STATUS);
    CloseServiceHandle(scm);
    if (!svc) { std::cout << "[?] Driver not installed\n"; return; }
    SERVICE_STATUS ss;
    QueryServiceStatus(svc, &ss);
    CloseServiceHandle(svc);
    const char* state = (ss.dwCurrentState == SERVICE_RUNNING)  ? "RUNNING" :
                        (ss.dwCurrentState == SERVICE_STOPPED)  ? "STOPPED" : "OTHER";
    std::cout << "[i] Driver status: " << state << "\n";
}

int main(int argc, char* argv[]) {
    std::cout << "=== TankAntiCheat KM Loader — SE315.Q21 ===\n";

    if (argc < 2) {
        std::cout << "Usage: loader.exe <load|unload|status>\n"
                  << "  load   — install + start driver\n"
                  << "  unload — stop + remove driver\n"
                  << "  status — check driver state\n";
        return 1;
    }

    std::string cmd = argv[1];

    if (cmd == "load") {
        // Tim duong dan toi .sys (cung thu muc voi loader.exe)
        char path[MAX_PATH];
        GetModuleFileNameA(nullptr, path, MAX_PATH);
        std::string dir(path);
        dir = dir.substr(0, dir.rfind('\\'));
        std::string sysPath = dir + "\\..\\anticheat_km.sys";

        // Chuyen sang absolute path
        char abs[MAX_PATH];
        GetFullPathNameA(sysPath.c_str(), MAX_PATH, abs, nullptr);
        std::cout << "[*] Loading: " << abs << "\n";
        return loadDriver(abs) ? 0 : 1;

    } else if (cmd == "unload") {
        return unloadDriver() ? 0 : 1;

    } else if (cmd == "status") {
        statusDriver(); return 0;

    } else {
        std::cerr << "[-] Unknown command: " << cmd << "\n";
        return 1;
    }
}
