#include "UdpServer.hpp"
#include "Utils/Logger.hpp"
#include <ws2tcpip.h>

bool UdpServer::init(int port) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        LOG_ERR("UdpServer: WSAStartup failed");
        return false;
    }

    _sock = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (_sock == INVALID_SOCKET) {
        LOG_ERR("UdpServer: socket() failed ({})", WSAGetLastError());
        return false;
    }

    // Non-blocking — recvfrom returns WSAEWOULDBLOCK instead of blocking
    u_long nonBlocking = 1;
    ioctlsocket(_sock, FIONBIO, &nonBlocking);

    sockaddr_in addr{};
    addr.sin_family      = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port        = htons(static_cast<u_short>(port));

    if (bind(_sock, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR) {
        LOG_ERR("UdpServer: bind failed on port {} ({})", port, WSAGetLastError());
        closesocket(_sock);
        _sock = INVALID_SOCKET;
        return false;
    }

    LOG_INFO("UdpServer: listening on UDP:{} (non-blocking)", port);
    return true;
}

void UdpServer::close() {
    if (_sock != INVALID_SOCKET) {
        closesocket(_sock);
        _sock = INVALID_SOCKET;
    }
    WSACleanup();
}

bool UdpServer::tryRecv(uint8_t* buf, int maxLen, sockaddr_in& outFrom, int& outLen) {
    int fromLen = sizeof(outFrom);
    int n = recvfrom(_sock,
                     reinterpret_cast<char*>(buf), maxLen, 0,
                     reinterpret_cast<sockaddr*>(&outFrom), &fromLen);
    if (n == SOCKET_ERROR) return false; // WSAEWOULDBLOCK = no data yet
    outLen = n;
    return true;
}

void UdpServer::send(const sockaddr_in& to, const uint8_t* data, int len) {
    sendto(_sock,
           reinterpret_cast<const char*>(data), len, 0,
           reinterpret_cast<const sockaddr*>(&to), sizeof(to));
}
