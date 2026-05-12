#include "VirtualPlayer.hpp"
#include <cstdlib>
#include <cstring>
#include <chrono>

using Clock = std::chrono::steady_clock;

VirtualPlayer::VirtualPlayer(uint32_t id,
                              const sockaddr_in& serverAddr,
                              Metrics& metrics)
    : _id(id), _server(serverAddr), _metrics(metrics)
{
    memset(_sendTimes, 0, sizeof(_sendTimes));
}

bool VirtualPlayer::init()
{
    return _sock.open(0); // non-blocking recv
}

void VirtualPlayer::sendLogin()
{
    int n = PacketBuilder::buildLogin(_buf, sizeof(_buf), _seq);
    if (n > 0) {
        int sent = _sock.sendTo(_server, _buf, n);
        if (sent > 0) {
            _metrics.packetsSent.fetch_add(1, std::memory_order_relaxed);
            _metrics.bytesOut   .fetch_add(sent, std::memory_order_relaxed);
            _sendTimes[_seq] = Clock::now();
            ++_seq;
        } else {
            _metrics.sendErrors.fetch_add(1, std::memory_order_relaxed);
        }
    }
    _loggedIn = true;
}

void VirtualPlayer::tick(float shootChance)
{
    // ── Send MOVE ───────────────────────────────────────────────────────────
    // Random direction: -1/0/+1 for both axes
    int8_t mx = static_cast<int8_t>((rand() % 3) - 1);
    int8_t mz = static_cast<int8_t>((rand() % 3) - 1);

    int n = PacketBuilder::buildMove(_buf, sizeof(_buf), _seq, _tick, mx, mz);
    if (n > 0) {
        int sent = _sock.sendTo(_server, _buf, n);
        if (sent > 0) {
            _sendTimes[_seq] = Clock::now();
            _metrics.packetsSent.fetch_add(1, std::memory_order_relaxed);
            _metrics.bytesOut   .fetch_add(sent, std::memory_order_relaxed);
            ++_seq;
        } else {
            _metrics.sendErrors.fetch_add(1, std::memory_order_relaxed);
        }
    }

    // ── Send SHOOT (probabilistic) ───────────────────────────────────────────
    if (static_cast<float>(rand()) / RAND_MAX < shootChance) {
        int ns = PacketBuilder::buildShoot(_buf, sizeof(_buf), _seq, _tick);
        if (ns > 0) {
            int sent = _sock.sendTo(_server, _buf, ns);
            if (sent > 0) {
                _metrics.packetsSent.fetch_add(1, std::memory_order_relaxed);
                _metrics.bytesOut   .fetch_add(sent, std::memory_order_relaxed);
                ++_seq;
            } else {
                _metrics.sendErrors.fetch_add(1, std::memory_order_relaxed);
            }
        }
    }

    ++_tick;

    // ── Drain recv buffer (non-blocking) ────────────────────────────────────
    // Server sends S2C_STATE_SYNC; if it does, measure RTT.
    // Currently server doesn't broadcast back to the specific client socket
    // (session wiring not complete), but we still drain to keep buffers clean
    // and measure latency when it does respond.
    for (;;) {
        int r = _sock.recvFrom(_recvBuf, sizeof(_recvBuf));
        if (r <= 0) break; // 0 = timeout (no data), -1 = error

        if (r > 0) {
            _metrics.packetsRecv.fetch_add(1, std::memory_order_relaxed);
            _metrics.bytesIn    .fetch_add(r, std::memory_order_relaxed);

            // First byte of response could carry the echoed seq for RTT
            // (requires server to echo seq in its reply – placeholder for now)
            if (r >= 1) {
                uint8_t echoSeq = _recvBuf[0];
                auto    sendT   = _sendTimes[echoSeq];
                if (sendT.time_since_epoch().count() != 0) {
                    auto rtt = std::chrono::duration_cast<std::chrono::microseconds>(
                        Clock::now() - sendT).count();
                    _metrics.recordLatencyUs(rtt);
                    _sendTimes[echoSeq] = {};
                }
            }
        }
    }
}
