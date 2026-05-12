#include "Metrics.hpp"
#include <cstdio>

void Metrics::recordLatencyUs(int64_t us)
{
    if      (us <  1000)  ++latBucket[0];
    else if (us <  5000)  ++latBucket[1];
    else if (us < 10000)  ++latBucket[2];
    else if (us < 50000)  ++latBucket[3];
    else if (us <100000)  ++latBucket[4];
    else                  ++latBucket[5];
}

void Metrics::printSnapshot(int elapsedSec,
                             uint64_t& prevSent,
                             uint64_t& prevRecv,
                             uint64_t& prevBytes) const
{
    uint64_t sent  = packetsSent.load(std::memory_order_relaxed);
    uint64_t recv  = packetsRecv.load(std::memory_order_relaxed);
    uint64_t errs  = sendErrors .load(std::memory_order_relaxed);
    uint64_t bout  = bytesOut   .load(std::memory_order_relaxed);

    uint64_t pps   = sent  - prevSent;
    uint64_t rps   = recv  - prevRecv;
    uint64_t bps   = bout  - prevBytes;

    prevSent  = sent;
    prevRecv  = recv;
    prevBytes = bout;

    printf("[%3ds]  sent=%7llu (%5llu pps)  recv=%7llu (%5llu rps)"
           "  err=%4llu  bw=%6llu KB/s\n",
           elapsedSec,
           (unsigned long long)sent,  (unsigned long long)pps,
           (unsigned long long)recv,  (unsigned long long)rps,
           (unsigned long long)errs,
           (unsigned long long)(bps / 1024));
    fflush(stdout);
}

void Metrics::printSummary(int totalSec) const
{
    uint64_t sent  = packetsSent.load();
    uint64_t recv  = packetsRecv.load();
    uint64_t errs  = sendErrors .load() + recvErrors.load();
    uint64_t bout  = bytesOut   .load();
    uint64_t bin_  = bytesIn    .load();

    printf("\n══════════════════════════════════════════\n");
    printf("  LOAD TEST SUMMARY  (%d seconds)\n", totalSec);
    printf("══════════════════════════════════════════\n");
    printf("  Packets sent     : %llu  (avg %llu pps)\n",
           (unsigned long long)sent,
           (unsigned long long)(totalSec > 0 ? sent / totalSec : 0));
    printf("  Packets received : %llu  (avg %llu pps)\n",
           (unsigned long long)recv,
           (unsigned long long)(totalSec > 0 ? recv / totalSec : 0));
    printf("  Send/recv errors : %llu\n",  (unsigned long long)errs);
    printf("  Bytes out        : %llu KB\n",(unsigned long long)(bout / 1024));
    printf("  Bytes in         : %llu KB\n",(unsigned long long)(bin_ / 1024));
    if (sent > 0)
        printf("  Loss rate        : %.2f%%\n",
               recv < sent ? 100.0 * (sent - recv) / (double)sent : 0.0);

    // Latency histogram (only meaningful if server echoes packets)
    uint64_t latTotal = 0;
    for (int i = 0; i < LAT_BUCKETS; ++i) latTotal += latBucket[i].load();
    if (latTotal > 0) {
        const char* labels[LAT_BUCKETS] = {
            "<1ms","1-5ms","5-10ms","10-50ms","50-100ms",">=100ms"
        };
        printf("\n  RTT latency histogram (%llu samples):\n",
               (unsigned long long)latTotal);
        for (int i = 0; i < LAT_BUCKETS; ++i) {
            uint64_t n = latBucket[i].load();
            printf("    %-9s : %6llu  (%.1f%%)\n",
                   labels[i],
                   (unsigned long long)n,
                   100.0 * n / (double)latTotal);
        }
    }
    printf("══════════════════════════════════════════\n\n");
    fflush(stdout);
}
