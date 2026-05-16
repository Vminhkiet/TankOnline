#include "Network/BlockingBackend.hpp"
#include "Network/Packets.hpp"
#include "Network/Opcode.hpp"
#include "Network/GameCommand.hpp"
#include "Memory/IoContext.hpp"
#include "Utils/Logger.hpp"
#include "ReadStream.h"
#include <ws2tcpip.h>

BlockingBackend::BlockingBackend(int numReceivers)
    : _numReceivers(numReceivers)
    , _pool(10'000)
{}

// ────────────────────────────────────────────────────────────────────────────

bool BlockingBackend::start(int port) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) {
        LOG_ERR("BlockingBackend: WSAStartup failed");
        return false;
    }

    _sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (_sock == INVALID_SOCKET) {
        LOG_ERR("BlockingBackend: socket() failed: {}", WSAGetLastError());
        WSACleanup(); return false;
    }

    // Socket is blocking by default — recvfrom will sleep until a datagram arrives.
    // (No ioctlsocket(FIONBIO) call — this is the key difference from the baseline
    //  non-blocking poll and from IOCP which uses WSA_FLAG_OVERLAPPED.)

    sockaddr_in addr{};
    addr.sin_family      = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port        = htons(static_cast<u_short>(port));

    if (bind(_sock, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR) {
        LOG_ERR("BlockingBackend: bind() failed on port {}: {}", port, WSAGetLastError());
        closesocket(_sock); _sock = INVALID_SOCKET;
        WSACleanup(); return false;
    }

    _running = true;
    _recvThreads.reserve(_numReceivers);
    for (int i = 0; i < _numReceivers; ++i)
        _recvThreads.emplace_back(&BlockingBackend::recvLoop, this);

    LOG_INFO("BlockingBackend: listening on port {} ({} blocking-recv threads)",
             port, _numReceivers);
    return true;
}

// ────────────────────────────────────────────────────────────────────────────

void BlockingBackend::stop() {
    if (!_running.exchange(false)) return;

    // Closing the socket unblocks all threads currently waiting in recvfrom().
    // They receive WSAENOTSOCK / WSAECONNRESET and exit their loops.
    if (_sock != INVALID_SOCKET) {
        closesocket(_sock);
        _sock = INVALID_SOCKET;
    }
    for (auto& t : _recvThreads) if (t.joinable()) t.join();
    _recvThreads.clear();
    WSACleanup();
}

// ────────────────────────────────────────────────────────────────────────────

void BlockingBackend::send(const sockaddr_in& target, const uint8_t* data, size_t len) {
    // Blocking sendto: if the OS send buffer is full, this call blocks the calling
    // thread until space is available. This is the Blocking Outbound module from spec.
    sendto(_sock,
           reinterpret_cast<const char*>(data), static_cast<int>(len), 0,
           reinterpret_cast<const sockaddr*>(&target), sizeof(target));
}

void BlockingBackend::setRouteCallback(std::function<void(GameCommand)> cb) {
    _routeCb = std::move(cb);
}

// ────────────────────────────────────────────────────────────────────────────
// recvLoop — one instance running per receiver thread.
//
// Flow (mirrors NetworkManager::workerThread + handleReader exactly):
//   1. Acquire IoContext from shared pool (same pre-allocated pool as IOCP path).
//   2. BLOCKING recvfrom — thread sleeps; OS wakes exactly ONE thread per datagram.
//      This is the contention point: with N threads and high pps, threads queue up
//      waiting for the kernel mutex on the socket receive buffer.
//   3. Decode PacketHeader (shared ReadStream / bit_packing code).
//   4. Build GameCommand and call routeCb (same path as IOCP worker).
//   5. Release IoContext back to pool immediately after copy — same as IOCP path.
// ────────────────────────────────────────────────────────────────────────────

void BlockingBackend::recvLoop() {
    while (_running) {
        IoContext* ctx = _pool.acquire(IoOperation::READ);

        int fromLen = sizeof(ctx->clientAddr);

        // ── BLOCKING CALL — the architectural variable ────────────────────────
        int n = recvfrom(_sock,
                         ctx->buffer, MAX_SIZE, 0,
                         reinterpret_cast<sockaddr*>(&ctx->clientAddr),
                         &fromLen);
        // ─────────────────────────────────────────────────────────────────────

        if (n <= 0) {
            _pool.release(ctx);
            // WSAENOTSOCK / error means stop() closed the socket — exit cleanly.
            if (!_running) break;
            continue;
        }

        // Decode header — identical to NetworkManager::handleReader
        ReadStream rs(reinterpret_cast<const uint32_t*>(ctx->buffer), n);
        PacketHeader hdr;
        if (!hdr.Serialize(rs)) {
            LOG_ERR("BlockingBackend: bad packet header");
            _pool.release(ctx);
            continue;
        }

        GameCommand cmd;
        cmd.sender    = ctx->clientAddr;
        cmd.matchId   = hdr.matchId;
        cmd.op        = hdr.opcode;
        cmd.rawBuffer.assign(ctx->buffer, ctx->buffer + n);

        // Release IoContext before routing — same as IOCP path
        _pool.release(ctx);

        if (_routeCb) _routeCb(std::move(cmd));
    }
}
