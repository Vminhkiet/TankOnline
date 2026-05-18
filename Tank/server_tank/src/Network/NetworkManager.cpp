#include "Network/NetworkManager.hpp"

NetworkManager::NetworkManager(size_t threadCount)
    : _pool(10000)
{
    _hCompPort = CreateIoCompletionPort(INVALID_HANDLE_VALUE, NULL, 0, 0);
    if (!_hCompPort)
        LOG_ERR("NetworkManager: failed to create IOCP port: {}", GetLastError());

    if (threadCount == 0)
        threadCount = std::thread::hardware_concurrency() * 2;
    _workers.reserve(threadCount);
}

NetworkManager::~NetworkManager() { stop(); }

void NetworkManager::stop() {
    _running = false;
    if (_hCompPort) { CloseHandle(_hCompPort); _hCompPort = NULL; }
    if (_serverSocket != INVALID_SOCKET) { closesocket(_serverSocket); _serverSocket = INVALID_SOCKET; }
    for (auto& t : _workers) if (t.joinable()) t.join();
}

bool NetworkManager::start(int port) {
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) return false;

    _serverSocket = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (_serverSocket == INVALID_SOCKET) {
        LOG_ERR("NetworkManager: socket() failed: {}", WSAGetLastError());
        WSACleanup(); return false;
    }

    sockaddr_in addr{};
    addr.sin_family      = AF_INET;
    addr.sin_addr.s_addr = INADDR_ANY;
    addr.sin_port        = htons(static_cast<u_short>(port));

    if (bind(_serverSocket, reinterpret_cast<sockaddr*>(&addr), sizeof(addr)) == SOCKET_ERROR) {
        LOG_ERR("NetworkManager: bind() failed: {}", WSAGetLastError());
        closesocket(_serverSocket); WSACleanup(); return false;
    }

    if (!CreateIoCompletionPort(reinterpret_cast<HANDLE>(_serverSocket),
                                _hCompPort, (ULONG_PTR)this, 0)) {
        LOG_ERR("NetworkManager: CreateIoCompletionPort assoc failed: {}", GetLastError());
        return false;
    }

    _running = true;

    size_t n = std::thread::hardware_concurrency() * 2;
    for (size_t i = 0; i < n; ++i)
        _workers.emplace_back(&NetworkManager::workerThread, this);

    // Pre-post receive buffers
    for (int i = 0; i < 100; ++i)
        postReceive(_pool.acquire(IoOperation::READ));

    LOG_INFO("NetworkManager: listening on port {} ({} IOCP workers)", port, n);
    return true;
}

bool NetworkManager::postReceive(IoContext* ctx) {
    if (!_running || !ctx) return false;
    ZeroMemory(&ctx->ov, sizeof(OVERLAPPED));
    ctx->wsaBuf.buf = ctx->buffer;
    ctx->wsaBuf.len = MAX_SIZE;

    DWORD bytes = 0, flags = 0;
    int r = WSARecvFrom(_serverSocket, &ctx->wsaBuf, 1, &bytes, &flags,
                        reinterpret_cast<sockaddr*>(&ctx->clientAddr),
                        &ctx->clientAddrLen, &ctx->ov, nullptr);
    if (r == SOCKET_ERROR && WSAGetLastError() != WSA_IO_PENDING) return false;
    return true;
}

void NetworkManager::workerThread() {
    while (_running) {
        DWORD      bytes = 0;
        ULONG_PTR  key   = 0;
        OVERLAPPED* ov   = nullptr;

        if (!GetQueuedCompletionStatus(_hCompPort, &bytes, &key, &ov, 500))
            continue;
        if (!ov) continue;

        auto* ctx = reinterpret_cast<IoContext*>(ov);
        handleReader(ctx, bytes);
    }
}

void NetworkManager::handleReader(IoContext* ctx, DWORD lengthBuf) {
    if (lengthBuf == 0) { postReceive(ctx); return; }

    // ── Parse timing: IOCP already delivered bytes — measure decode cost ──────
    using Clock  = std::chrono::high_resolution_clock;
    using Micros = std::chrono::microseconds;
    auto t_parse_start = Clock::now();

#ifdef DEBUG_NETWORK
    LOG_INFO("NetworkManager: recv {} bytes from {}:{}", lengthBuf,
             ntohl(ctx->clientAddr.sin_addr.s_addr), ntohs(ctx->clientAddr.sin_port));
#endif

    ReadStream rs(reinterpret_cast<const uint32_t*>(ctx->buffer),
                  static_cast<int>(lengthBuf));

    PacketHeader hdr;
    if (!hdr.Serialize(rs)) {
        LOG_ERR("NetworkManager: bad packet header from {}:{}",
                ntohl(ctx->clientAddr.sin_addr.s_addr), ntohs(ctx->clientAddr.sin_port));
        postReceive(ctx); return;
    }

#ifdef DEBUG_NETWORK
    LOG_INFO("NetworkManager: parsed header → matchId={} opcode={}",
             hdr.matchId, static_cast<uint16_t>(hdr.opcode));
#endif

    GameCommand cmd;
    cmd.sender   = ctx->clientAddr;
    cmd.matchId  = hdr.matchId;
    cmd.op       = hdr.opcode;
    cmd.rawBuffer.assign(ctx->buffer, ctx->buffer + lengthBuf);

    postReceive(ctx);

    auto parseUs = std::chrono::duration_cast<Micros>(Clock::now() - t_parse_start).count();
    _accumRecvParseUs.fetch_add(static_cast<uint64_t>(parseUs), std::memory_order_relaxed);
    _recvCount.fetch_add(1, std::memory_order_relaxed);
    // ─────────────────────────────────────────────────────────────────────────

    if (_routeCb) _routeCb(std::move(cmd));
}

void NetworkManager::drainRecvStats(uint64_t& sumUs, uint32_t& count) {
    sumUs = _accumRecvParseUs.exchange(0, std::memory_order_relaxed);
    count = _recvCount.exchange(0, std::memory_order_relaxed);
}

void NetworkManager::send(const sockaddr_in& target, const uint8_t* data, size_t len) {
    sendto(_serverSocket,
           reinterpret_cast<const char*>(data), static_cast<int>(len),
           0,
           reinterpret_cast<const sockaddr*>(&target), sizeof(target));
}
