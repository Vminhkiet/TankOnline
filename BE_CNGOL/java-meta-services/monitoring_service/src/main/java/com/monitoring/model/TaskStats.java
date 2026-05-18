package com.monitoring.model;

import lombok.Data;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Thread-safe accumulator cho per-match task timing.
 * Lưu giá trị mới nhất từ mỗi Kafka event (window 600 ticks đã average ở Python agent).
 */
@Data
public class TaskStats {

    private final int matchId;
    private final AtomicLong bulletUs  = new AtomicLong(0);
    private final AtomicLong physicsUs = new AtomicLong(0);
    private final AtomicLong snapUs    = new AtomicLong(0);
    private volatile long lastUpdated  = System.currentTimeMillis();

    public TaskStats(int matchId) {
        this.matchId = matchId;
    }

    public void update(long bullet, long physics, long snap) {
        bulletUs.set(bullet);
        physicsUs.set(physics);
        snapUs.set(snap);
        lastUpdated = System.currentTimeMillis();
    }

    /** Match được coi là stale nếu không có update trong 60 giây. */
    public boolean isStale() {
        return System.currentTimeMillis() - lastUpdated > 60_000;
    }

    public long getBulletUs()  { return bulletUs.get(); }
    public long getPhysicsUs() { return physicsUs.get(); }
    public long getSnapUs()    { return snapUs.get(); }
}
