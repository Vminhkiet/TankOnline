#pragma once
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <cstdint>
#include <string>

// RAII non-blocking UDP socket
class UdpSocket {
public:
    UdpSocket()  = default;
    ~UdpSocket() { close(); }

    UdpSocket(const UdpSocket&)            = delete;
    UdpSocket& operator=(const UdpSocket&) = delete;

    // Bind to any local port; set recv timeout to recvTimeoutMs (0 = non-blocking)
    bool open(int recvTimeoutMs = 0);
    void close();

    bool isOpen() const { return _sock != INVALID_SOCKET; }

    // Returns bytes sent, or -1 on error
    int sendTo(const sockaddr_in& addr, const void* data, int len);

    // Returns bytes received (> 0), 0 on timeout/no-data, -1 on error
    // fromAddr may be nullptr
    int recvFrom(void* buf, int bufLen, sockaddr_in* fromAddr = nullptr);

private:
    SOCKET _sock = INVALID_SOCKET;
};

sockaddr_in makeAddr(const std::string& ip, uint16_t port);
