#pragma once
#include <winsock2.h>
#include <cstdint>

// Non-blocking UDP socket (ioctlsocket FIONBIO).
// No IOCP, no OVERLAPPED — plain recvfrom/sendto.
class UdpServer {
public:
    bool init(int port);
    void close();

    // Returns false when no packet is available (WSAEWOULDBLOCK) or on error.
    bool tryRecv(uint8_t* buf, int maxLen, sockaddr_in& outFrom, int& outLen);
    void send(const sockaddr_in& to, const uint8_t* data, int len);

private:
    SOCKET _sock = INVALID_SOCKET;
};
