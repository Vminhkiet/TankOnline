/**
 * k6 stress test — Tank Online Match Simulation
 *
 * Flow mỗi VU (Virtual User):
 *   1. Login cặp stress users (stressXX_A + stressXX_B)
 *   2. Cả 2 đồng thời gọi /api/matchmaking/find → server tạo match
 *   3. Log matchId + playerId
 *   4. Lặp lại theo stages
 *
 * Sau khi matches được tạo, Python agent đọc [Perf] log →
 * Prometheus → Grafana hiển thị active_matches > 0
 */

import http from "k6/http";
import { check, sleep } from "k6";
import { Counter, Trend, Rate, Gauge } from "k6/metrics";
import { SharedArray } from "k6/data";

// ── Config ────────────────────────────────────────────────────────────────────
const BASE = "http://localhost:8080";

// stress01..stress20 → 10 cặp
const USER_PAIRS = new SharedArray("pairs", function () {
  const pairs = [];
  for (let i = 1; i <= 20; i += 2) {
    pairs.push({
      a: `stress${String(i).padStart(2, "0")}`,
      b: `stress${String(i + 1).padStart(2, "0")}`,
    });
  }
  return pairs; // 10 cặp
});

// ── Custom metrics ─────────────────────────────────────────────────────────────
const matchCreated    = new Counter("tank_matches_created");
const matchFailed     = new Counter("tank_matches_failed");
const loginLatency    = new Trend("tank_login_latency_ms",    true);
const matchmakeLatency= new Trend("tank_matchmaking_latency_ms", true);
const loginSuccess    = new Rate("tank_login_success_rate");
const matchSuccess    = new Rate("tank_matchmaking_success_rate");

// ── Load stages ───────────────────────────────────────────────────────────────
export const options = {
  scenarios: {
    match_ramp: {
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "15s", target: 2  },  // warm-up: 1 cặp
        { duration: "20s", target: 6  },  // ramp: 3 cặp đồng thời
        { duration: "30s", target: 10 },  // peak: 5 cặp
        { duration: "20s", target: 4  },  // ramp-down
        { duration: "15s", target: 0  },  // cool-down
      ],
    },
  },
  thresholds: {
    tank_login_success_rate:      ["rate>0.95"],
    tank_matchmaking_success_rate: ["rate>0.80"],
    tank_login_latency_ms:        ["p(95)<500"],
    tank_matchmaking_latency_ms:  ["p(95)<200"],
    http_req_failed:               ["rate<0.10"],
  },
};

// ── Helpers ───────────────────────────────────────────────────────────────────
function login(username) {
  const t0 = Date.now();
  const res = http.post(
    `${BASE}/api/auth/login`,
    JSON.stringify({ username, password: "password123" }),
    { headers: { "Content-Type": "application/json" }, timeout: "15s" }
  );
  loginLatency.add(Date.now() - t0);

  const ok = check(res, {
    "login 200": (r) => r.status === 200,
    "has jwt":   (r) => {
      try { return !!JSON.parse(r.body).jwt; } catch { return false; }
    },
  });
  loginSuccess.add(ok);
  if (!ok) return null;
  return JSON.parse(res.body).jwt;
}

function findMatch(jwt) {
  const t0 = Date.now();
  const res = http.post(
    `${BASE}/api/matchmaking/find`,
    "{}",
    {
      headers: {
        "Content-Type":  "application/json",
        "Authorization": `Bearer ${jwt}`,
      },
      timeout: "30s",
    }
  );
  matchmakeLatency.add(Date.now() - t0);

  const ok = check(res, {
    "match 200":    (r) => r.status === 200,
    "has matchId":  (r) => {
      try { return !!JSON.parse(r.body).matchId; } catch { return false; }
    },
  });
  matchSuccess.add(ok);
  if (!ok) return null;
  return JSON.parse(res.body);
}

// ── Main VU scenario ──────────────────────────────────────────────────────────
export default function () {
  // Mỗi VU nhận 1 cặp user (round-robin theo __VU index)
  const pair = USER_PAIRS[(__VU - 1) % USER_PAIRS.length];

  // Step 1 — Login cả 2 players
  const [jwtA, jwtB] = [login(pair.a), login(pair.b)];
  if (!jwtA || !jwtB) {
    matchFailed.add(1);
    sleep(2);
    return;
  }

  // Step 2 — Cả 2 gọi matchmaking đồng thời (batch request)
  const responses = http.batch([
    {
      method: "POST", url: `${BASE}/api/matchmaking/find`,
      body: "{}",
      params: {
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${jwtA}` },
        timeout: "30s",
      },
    },
    {
      method: "POST", url: `${BASE}/api/matchmaking/find`,
      body: "{}",
      params: {
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${jwtB}` },
        timeout: "30s",
      },
    },
  ]);

  let matched = false;
  for (const res of responses) {
    matchmakeLatency.add(res.timings.duration);
    try {
      const body = JSON.parse(res.body);
      if (res.status === 200 && body.matchId) {
        matched = true;
        matchCreated.add(1);
        console.log(
          `✓ VU${__VU} matched: matchId=${body.matchId} playerId=${body.playerId}`
        );
      }
    } catch (_) {}
  }
  matchSuccess.add(matched);
  if (!matched) matchFailed.add(1);

  // Step 3 — Giữ VU sống để server duy trì match (simulate player online)
  // Match sẽ timeout sau 5s không có UDP → VU sleep ngắn rồi re-matchmake
  sleep(Math.random() * 3 + 2);
}

// ── Summary hiển thị sau khi test xong ───────────────────────────────────────
export function handleSummary(data) {
  const m = data.metrics;

  const fmt = (metric, stat) =>
    m[metric] ? m[metric].values[stat]?.toFixed(1) ?? "-" : "-";

  const summary = `
╔══════════════════════════════════════════════════════════╗
║       k6 Tank Online Match Stress — Summary              ║
╚══════════════════════════════════════════════════════════╝
  Duration      : ${data.state.testRunDurationMs / 1000}s
  VUs peak      : ${data.state.isFullIteration ? "completed" : ""}

  LOGIN
    Success rate : ${fmt("tank_login_success_rate", "rate")}
    p50 latency  : ${fmt("tank_login_latency_ms", "p(50)")} ms
    p95 latency  : ${fmt("tank_login_latency_ms", "p(95)")} ms

  MATCHMAKING
    Matches created : ${m["tank_matches_created"]?.values["count"] ?? 0}
    Failed          : ${m["tank_matches_failed"]?.values["count"] ?? 0}
    Success rate    : ${fmt("tank_matchmaking_success_rate", "rate")}
    p50 latency     : ${fmt("tank_matchmaking_latency_ms", "p(50)")} ms
    p95 latency     : ${fmt("tank_matchmaking_latency_ms", "p(95)")} ms

  HTTP
    Total requests  : ${m["http_reqs"]?.values["count"] ?? 0}
    Failed          : ${m["http_req_failed"]?.values["rate"]?.toFixed(3) ?? "-"}

  → Grafana: http://localhost:3000/d/tank-cpp-server
  → Dashboard 2: http://localhost:3000/d/tank-java-services
╚══════════════════════════════════════════════════════════╝
`;
  console.log(summary);
  return { stdout: summary };
}
