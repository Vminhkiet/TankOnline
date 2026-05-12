#include "UdpSocket.hpp"
#include <cstring>
#include <cstdio>

bool UdpSocket::open(int recvTimeoutMs)
{
    _sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (_sock == INVALID_SOCKET) return false;

    // Bind to any port so the OS assigns a unique source port
    sockaddr_in local{};
    local.sin_family      = AF_INET;
    local.sin_addr.s_addr = INADDR_ANY;
    local.sin_port        = 0; // OS picks
    if (bind(_sock, reinterpret_cast<sockaddr*>(&local), sizeof(local)) == SOCKET_ERROR) {
        closesocket(_sock); _sock = INVALID_SOCKET; return false;
    }

    // Set receive timeout (0 ms = non-blocking poll via SO_RCVTIMEO of 1 ms)
    DWORD tv = recvTimeoutMs <= 0 ? 1 : static_cast<DWORD>(recvTimeoutMs);
    setsockopt(_sock, SOL_SOCKET, SO_RCVTIMEO,
               reinterpret_cast<const char*>(&tv), sizeof(tv));

    return true;
}

void UdpSocket::close()
{
    if (_sock != INVALID_SOCKET) {
        closesocket(_sock);
        _sock = INVALID_SOCKET;
    }
}

int UdpSocket::sendTo(const sockaddr_in& addr, const void* data, int len)
{
    int sent = sendto(_sock, reinterpret_cast<const char*>(data), len, 0,
                      reinterpret_cast<const sockaddr*>(&addr), sizeof(addr));
    return sent; // -1 on error
}

int UdpSocket::recvFrom(void* buf, int bufLen, sockaddr_in* fromAddr)
{
    sockaddr_in from{};
    int fromLen = sizeof(from);
    int n = recvfrom(_sock, reinterpret_cast<char*>(buf), bufLen, 0,
                     reinterpret_cast<sockaddr*>(&from), &fromLen);
    if (n == SOCKET_ERROR) {
        int err = WSAGetLastError();
        // WSAETIMEDOUT / WSAEWOULDBLOCK = no data available (not an error)
        if (err == WSAETIMEDOUT || err == WSAEWOULDBLOCK) return 0;
        return -1;
    }
    if (fromAddr) *fromAddr = from;
    return n;
}

sockaddr_in makeAddr(const std::string& ip, uint16_t port)
{
    sockaddr_in addr{};
    addr.sin_family = AF_INET;
    addr.sin_port   = htons(port);
    inet_pton(AF_INET, ip.c_str(), &addr.sin_addr);
    return addr;
}
