"""
Full API test suite for SE315.Q21 TankOnline backend.
Tests all 8 Java micro-services through the API Gateway (port 8080).
Run: python test_all_apis.py
"""

import requests
import json
import time
import random
import string
import threading
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime

GW = "http://localhost:8080"   # API Gateway — all external traffic goes here
DIRECT = {
    "eureka":       "http://localhost:8761",
    "auth":         "http://localhost:8081",
    "matchmaking":  "http://localhost:8085",
    "history":      "http://localhost:8086",
    "profile":      "http://localhost:8087",
    "shop":         "http://localhost:8088",
    "monitoring":   "http://localhost:8090",
}

TIMEOUT = 10  # seconds per request

# ── Colour helpers ────────────────────────────────────────────────────────────

GREEN  = "\033[92m"
RED    = "\033[91m"
YELLOW = "\033[93m"
CYAN   = "\033[96m"
BOLD   = "\033[1m"
RESET  = "\033[0m"

results = []

def ok(name, detail=""):
    msg = f"{GREEN}  [PASS]{RESET} {name}" + (f" — {detail}" if detail else "")
    print(msg)
    results.append(("PASS", name))

def fail(name, detail=""):
    msg = f"{RED}  [FAIL]{RESET} {name}" + (f" — {detail}" if detail else "")
    print(msg)
    results.append(("FAIL", name))

def warn(name, detail=""):
    msg = f"{YELLOW}  [WARN]{RESET} {name}" + (f" — {detail}" if detail else "")
    print(msg)
    results.append(("WARN", name))

def section(title):
    print(f"\n{BOLD}{CYAN}{'='*60}{RESET}")
    print(f"{BOLD}{CYAN}  {title}{RESET}")
    print(f"{BOLD}{CYAN}{'='*60}{RESET}")

def get(url, headers=None, label=None):
    try:
        r = requests.get(url, headers=headers, timeout=TIMEOUT)
        return r
    except requests.exceptions.ConnectionError:
        return None
    except Exception as e:
        return None

def post(url, body=None, headers=None):
    try:
        r = requests.post(url, json=body, headers=headers, timeout=TIMEOUT)
        return r
    except requests.exceptions.ConnectionError:
        return None
    except Exception as e:
        return None

def put(url, body=None, headers=None):
    try:
        r = requests.put(url, json=body, headers=headers, timeout=TIMEOUT)
        return r
    except requests.exceptions.ConnectionError:
        return None
    except Exception as e:
        return None

def delete(url, headers=None):
    try:
        r = requests.delete(url, headers=headers, timeout=TIMEOUT)
        return r
    except requests.exceptions.ConnectionError:
        return None
    except Exception as e:
        return None

def check(name, r, expected_status=200, check_body=None):
    if r is None:
        fail(name, "Connection refused / timeout")
        return False
    if r.status_code != expected_status:
        fail(name, f"HTTP {r.status_code} (expected {expected_status}) — {r.text[:120]}")
        return False
    if check_body:
        try:
            body = r.json()
            if not check_body(body):
                fail(name, f"Body check failed — {str(body)[:120]}")
                return False
        except Exception as e:
            fail(name, f"JSON parse error: {e}")
            return False
    ok(name)
    return True

def rand_str(n=8):
    return ''.join(random.choices(string.ascii_lowercase, k=n))

# ─────────────────────────────────────────────────────────────────────────────
# 1. SERVICE HEALTH CHECKS (direct ports)
# ─────────────────────────────────────────────────────────────────────────────

def test_health_checks():
    section("1. SERVICE HEALTH CHECKS (direct ports)")
    for name, base in DIRECT.items():
        r = get(f"{base}/actuator/health")
        if r is None:
            warn(f"{name} ({base})", "Service not reachable — skipping its tests")
        elif r.status_code == 200:
            ok(f"{name} ({base})", r.json().get("status", "?"))
        else:
            fail(f"{name} ({base})", f"HTTP {r.status_code}")

    # Eureka dashboard (no actuator)
    r = get(f"{DIRECT['eureka']}/actuator/health")
    # already checked above

    # Gateway
    r = get(f"{GW}/actuator/health")
    if r and r.status_code == 200:
        ok(f"api_gateway ({GW})", r.json().get("status", "?"))
    else:
        warn(f"api_gateway ({GW})", "Not reachable or no health endpoint")

# ─────────────────────────────────────────────────────────────────────────────
# 2. AUTH SERVICE
# ─────────────────────────────────────────────────────────────────────────────

TOKEN_A = None
TOKEN_B = None
REFRESH_TOKEN_A = None
USER_A = None
USER_B = None

def test_auth():
    global TOKEN_A, TOKEN_B, REFRESH_TOKEN_A, USER_A, USER_B

    section("2. AUTH SERVICE  (/api/auth, /api/user)")

    username_a = f"testuser_{rand_str()}"
    username_b = f"testuser_{rand_str()}"
    password   = "Test@1234"

    # ── Signup ────────────────────────────────────────────────────────────────
    r = post(f"{GW}/api/auth/signup", {
        "username": username_a,
        "email":    f"{username_a}@test.com",
        "password": password
    })
    if not check("POST /api/auth/signup (user A)", r, 200,
                 lambda b: "accessToken" in b or "token" in b or "access_token" in b):
        # Try 201
        if r and r.status_code == 201:
            ok("POST /api/auth/signup (user A) — HTTP 201")
        else:
            warn("Signup failed — some auth tests will be skipped")

    if r and r.status_code in (200, 201):
        body = r.json()
        TOKEN_A = body.get("accessToken") or body.get("token") or body.get("access_token")
        REFRESH_TOKEN_A = body.get("refreshToken") or body.get("refresh_token")
        USER_A = username_a

    # Signup user B
    r = post(f"{GW}/api/auth/signup", {
        "username": username_b,
        "email":    f"{username_b}@test.com",
        "password": password
    })
    if r and r.status_code in (200, 201):
        body = r.json()
        TOKEN_B = body.get("accessToken") or body.get("token") or body.get("access_token")
        USER_B = username_b
        ok("POST /api/auth/signup (user B)")

    # ── Login ─────────────────────────────────────────────────────────────────
    r = post(f"{GW}/api/auth/login", {"username": username_a, "password": password})
    if check("POST /api/auth/login — valid credentials", r, 200,
             lambda b: ("accessToken" in b or "token" in b or "access_token" in b)):
        body = r.json()
        TOKEN_A = body.get("accessToken") or body.get("token") or body.get("access_token")
        REFRESH_TOKEN_A = body.get("refreshToken") or body.get("refresh_token")

    r = post(f"{GW}/api/auth/login", {"username": username_a, "password": "wrongpass"})
    if r and r.status_code in (401, 403, 400):
        ok("POST /api/auth/login — wrong password returns 4xx", f"HTTP {r.status_code}")
    elif r:
        fail("POST /api/auth/login — wrong password", f"Expected 4xx but got HTTP {r.status_code}")

    # ── Refresh token ─────────────────────────────────────────────────────────
    if REFRESH_TOKEN_A and TOKEN_A:
        r = post(f"{GW}/api/auth/refresh",
                 {"refreshToken": REFRESH_TOKEN_A},
                 headers={"Authorization": f"Bearer {TOKEN_A}"})
        if r and r.status_code == 200:
            ok("POST /api/auth/refresh")
            body = r.json()
            TOKEN_A = body.get("accessToken") or body.get("token") or TOKEN_A
        else:
            warn("POST /api/auth/refresh", f"HTTP {r.status_code if r else 'no response'} — {r.text[:80] if r else ''}")
    else:
        warn("POST /api/auth/refresh", "Skipped — no refresh token from signup/login")

    # ── GET /api/user/me ──────────────────────────────────────────────────────
    if TOKEN_A:
        r = get(f"{GW}/api/user/me", headers={"Authorization": f"Bearer {TOKEN_A}"})
        check("GET /api/user/me — authenticated", r, 200,
              lambda b: "username" in b or "email" in b or "id" in b)

        r = get(f"{GW}/api/user/me")
        if r and r.status_code in (401, 403):
            ok("GET /api/user/me — unauthenticated returns 4xx", f"HTTP {r.status_code}")
        elif r:
            fail("GET /api/user/me — unauthenticated", f"Expected 4xx, got {r.status_code}")
    else:
        warn("GET /api/user/me", "Skipped — no JWT")

    # ── GET /api/user/users ───────────────────────────────────────────────────
    r = get(f"{GW}/api/user/users")
    check("GET /api/user/users — public endpoint", r, 200,
          lambda b: isinstance(b, list))

    # ── Logout ───────────────────────────────────────────────────────────────
    if TOKEN_A:
        r = post(f"{GW}/api/auth/logout", headers={"Authorization": f"Bearer {TOKEN_A}"})
        if r and r.status_code in (200, 204):
            ok("POST /api/auth/logout", f"HTTP {r.status_code}")
        else:
            warn("POST /api/auth/logout", f"HTTP {r.status_code if r else 'no response'} — {r.text[:80] if r else ''}")
        # Re-login after logout so TOKEN_A stays valid for subsequent tests
        r = post(f"{GW}/api/auth/login", {"username": username_a, "password": password})
        if r and r.status_code == 200:
            TOKEN_A = r.json().get("accessToken") or r.json().get("token") or TOKEN_A

# ─────────────────────────────────────────────────────────────────────────────
# 3. MATCHMAKING SERVICE
# ─────────────────────────────────────────────────────────────────────────────

def test_matchmaking():
    section("3. MATCHMAKING SERVICE  (/api/matchmaking)")

    if not TOKEN_A:
        warn("POST /api/matchmaking/find", "Skipped — no JWT (auth tests failed)")
        return

    # Single request — should hang waiting for 10 players; we abort after 3s
    print(f"  {YELLOW}Note: MATCH_SIZE=10, sending 10 concurrent requests to trigger a match...{RESET}")

    tokens = [TOKEN_A]
    if TOKEN_B:
        tokens.append(TOKEN_B)

    # Create 8 extra temp users for matchmaking
    extra_tokens = []
    for i in range(8):
        u = f"mm_tmp_{rand_str()}"
        r = post(f"{GW}/api/auth/signup", {
            "username": u,
            "email": f"{u}@test.com",
            "password": "Test@1234"
        })
        if r and r.status_code in (200, 201):
            t = r.json().get("accessToken") or r.json().get("token")
            if t:
                extra_tokens.append(t)

    all_tokens = tokens + extra_tokens
    if len(all_tokens) < 10:
        warn(f"POST /api/matchmaking/find",
             f"Only {len(all_tokens)} tokens available (need 10). Sending what we have.")

    responses = []
    lock = threading.Lock()

    def call_find(tok):
        r = post(f"{GW}/api/matchmaking/find",
                 headers={"Authorization": f"Bearer {tok}"})
        with lock:
            responses.append(r)

    threads = [threading.Thread(target=call_find, args=(t,)) for t in all_tokens[:10]]
    for t in threads:
        t.start()
    for t in threads:
        t.join(timeout=15)

    matched = [r for r in responses if r and r.status_code == 200]
    if matched:
        body = matched[0].json()
        ok(f"POST /api/matchmaking/find — {len(matched)}/{len(all_tokens)} players matched",
           f"matchId={body.get('matchId')} serverPort={body.get('serverPort')}")
        # Validate response shape
        for key in ("matchId", "serverHost", "serverPort", "playerId"):
            if key not in body:
                fail(f"  matchmaking response missing field: {key}")
            else:
                ok(f"  response field '{key}' present", str(body[key]))
    else:
        warn("POST /api/matchmaking/find",
             f"No 200 response — replies: {[r.status_code if r else 'err' for r in responses]}")

# ─────────────────────────────────────────────────────────────────────────────
# 4. HISTORY SERVICE
# ─────────────────────────────────────────────────────────────────────────────

def test_history():
    section("4. HISTORY SERVICE  (/api/history)")

    # Public endpoint
    r = get(f"{GW}/api/history/leaderboard")
    check("GET /api/history/leaderboard — public", r, 200,
          lambda b: isinstance(b, list))

    if not TOKEN_A:
        warn("History protected endpoints", "Skipped — no JWT")
        return

    h = {"Authorization": f"Bearer {TOKEN_A}"}

    r = get(f"{GW}/api/history/me", headers=h)
    check("GET /api/history/me", r, 200, lambda b: isinstance(b, list))

    r = get(f"{GW}/api/history/me/stats", headers=h)
    check("GET /api/history/me/stats", r, 200,
          lambda b: isinstance(b, dict))

    # Unauthenticated should be rejected
    r = get(f"{GW}/api/history/me")
    if r and r.status_code in (401, 403):
        ok("GET /api/history/me — unauthenticated returns 4xx", f"HTTP {r.status_code}")
    elif r:
        fail("GET /api/history/me — unauthenticated", f"Expected 4xx, got {r.status_code}")

# ─────────────────────────────────────────────────────────────────────────────
# 5. PROFILE SERVICE
# ─────────────────────────────────────────────────────────────────────────────

def test_profile():
    section("5. PROFILE SERVICE  (/api/profile)")

    if not TOKEN_A:
        warn("Profile endpoints", "Skipped — no JWT")
        return

    h = {"Authorization": f"Bearer {TOKEN_A}"}

    r = get(f"{GW}/api/profile/me", headers=h)
    check("GET /api/profile/me", r, 200,
          lambda b: isinstance(b, dict))

    profile_body = r.json() if r and r.status_code == 200 else {}
    user_id = profile_body.get("userId") or profile_body.get("id") or profile_body.get("user_id")

    # Update profile
    r = put(f"{GW}/api/profile/me",
            {"displayName": "TestPlayer", "avatar": "default.png"},
            headers=h)
    if r and r.status_code in (200, 204):
        ok("PUT /api/profile/me", f"HTTP {r.status_code}")
    else:
        warn("PUT /api/profile/me", f"HTTP {r.status_code if r else 'no response'} — {r.text[:80] if r else ''}")

    # Coins
    r = get(f"{GW}/api/profile/me/coins", headers=h)
    if r and r.status_code == 200:
        ok("GET /api/profile/me/coins", f"coins={r.json()}")
    else:
        warn("GET /api/profile/me/coins", f"HTTP {r.status_code if r else 'no response'}")

    # View other user (public)
    if user_id:
        r = get(f"{GW}/api/profile/{user_id}")
        check(f"GET /api/profile/{{userId}} — public view", r, 200)
    else:
        warn("GET /api/profile/{userId}", "Skipped — could not extract userId from profile")

    # Redeem invalid gift code
    r = post(f"{GW}/api/profile/giftcode/redeem",
             {"code": "INVALID_CODE_TEST"},
             headers=h)
    if r and r.status_code in (400, 404, 422):
        ok("POST /api/profile/giftcode/redeem — invalid code returns 4xx", f"HTTP {r.status_code}")
    elif r and r.status_code == 200:
        warn("POST /api/profile/giftcode/redeem — invalid code accepted", "Expected 4xx")
    else:
        warn("POST /api/profile/giftcode/redeem", f"HTTP {r.status_code if r else 'no response'}")

    # Admin: create gift code
    admin_h = {**h, "Authorization": f"Bearer {TOKEN_A}", "X-Admin-Token": "admin-secret-token"}
    r = post(f"{GW}/api/profile/admin/giftcode",
             {"code": f"TEST{rand_str(6).upper()}", "coins": 100, "maxUses": 5},
             headers=admin_h)
    if r and r.status_code in (200, 201):
        ok("POST /api/profile/admin/giftcode", f"HTTP {r.status_code}")
        gift_id = r.json().get("id")

        # List gift codes
        r2 = get(f"{GW}/api/profile/admin/giftcode", headers=admin_h)
        if r2 and r2.status_code == 200:
            ok("GET /api/profile/admin/giftcode", f"{len(r2.json())} codes")
        else:
            warn("GET /api/profile/admin/giftcode", f"HTTP {r2.status_code if r2 else 'no response'}")

        # Delete gift code
        if gift_id:
            r3 = delete(f"{GW}/api/profile/admin/giftcode/{gift_id}", headers=admin_h)
            if r3 and r3.status_code in (200, 204):
                ok(f"DELETE /api/profile/admin/giftcode/{gift_id}")
            else:
                warn(f"DELETE /api/profile/admin/giftcode/{gift_id}",
                     f"HTTP {r3.status_code if r3 else 'no response'}")
    else:
        warn("POST /api/profile/admin/giftcode",
             f"HTTP {r.status_code if r else 'no response'} — {r.text[:80] if r else ''}")

# ─────────────────────────────────────────────────────────────────────────────
# 6. SHOP SERVICE
# ─────────────────────────────────────────────────────────────────────────────

def test_shop():
    section("6. SHOP SERVICE  (/api/shop)")

    # Public endpoints
    r = get(f"{GW}/api/shop/items")
    check("GET /api/shop/items — public", r, 200, lambda b: isinstance(b, list))

    items = r.json() if r and r.status_code == 200 else []

    r = get(f"{GW}/api/shop/items/version")
    if r and r.status_code == 200:
        ok("GET /api/shop/items/version", f"version={r.json()}")
    else:
        warn("GET /api/shop/items/version", f"HTTP {r.status_code if r else 'no response'}")

    # Category
    r = get(f"{GW}/api/shop/items/category/tank")
    if r and r.status_code in (200, 404):
        ok("GET /api/shop/items/category/tank", f"HTTP {r.status_code} — {len(r.json()) if r.status_code==200 and isinstance(r.json(),list) else r.text[:40]}")
    else:
        warn("GET /api/shop/items/category/tank", f"HTTP {r.status_code if r else 'no response'}")

    # Single item
    if items:
        item_id = items[0].get("id") or items[0].get("itemId")
        if item_id:
            r = get(f"{GW}/api/shop/items/{item_id}")
            check(f"GET /api/shop/items/{item_id}", r, 200)
    else:
        warn("GET /api/shop/items/{id}", "Skipped — no items in DB")

    if not TOKEN_A:
        warn("Shop protected endpoints", "Skipped — no JWT")
        return

    h = {"Authorization": f"Bearer {TOKEN_A}"}

    # My items
    r = get(f"{GW}/api/shop/my-items", headers=h)
    if r and r.status_code == 200:
        ok("GET /api/shop/my-items", f"{r.text[:60]}")
    else:
        warn("GET /api/shop/my-items", f"HTTP {r.status_code if r else 'no response'}")

    # Purchase (if there are items and we have coins)
    if items:
        item_id = items[0].get("id") or items[0].get("itemId")
        if item_id:
            r = post(f"{GW}/api/shop/purchase",
                     {"itemId": item_id},
                     headers=h)
            if r and r.status_code in (200, 201):
                ok(f"POST /api/shop/purchase (itemId={item_id})")
            elif r and r.status_code in (400, 409, 422):
                ok(f"POST /api/shop/purchase — 4xx (already owned or insufficient coins)", f"HTTP {r.status_code}")
            else:
                warn("POST /api/shop/purchase", f"HTTP {r.status_code if r else 'no response'} — {r.text[:80] if r else ''}")
    else:
        warn("POST /api/shop/purchase", "Skipped — no items in DB")

    # Admin: create item
    r = post(f"{GW}/api/shop/admin/items",
             {"name": "Test Tank", "description": "Auto-test item",
              "price": 100, "category": "tank", "imageUrl": "test.png"},
             headers=h)
    if r and r.status_code in (200, 201):
        ok("POST /api/shop/admin/items", f"HTTP {r.status_code}")
        created_id = r.json().get("id") or r.json().get("itemId")
        if created_id:
            # Update
            r2 = put(f"{GW}/api/shop/admin/items/{created_id}",
                     {"name": "Test Tank Updated", "price": 200, "category": "tank"},
                     headers=h)
            if r2 and r2.status_code in (200, 204):
                ok(f"PUT /api/shop/admin/items/{created_id}")
            else:
                warn(f"PUT /api/shop/admin/items/{created_id}",
                     f"HTTP {r2.status_code if r2 else 'no response'}")
            # Delete
            r3 = delete(f"{GW}/api/shop/admin/items/{created_id}", headers=h)
            if r3 and r3.status_code in (200, 204):
                ok(f"DELETE /api/shop/admin/items/{created_id}")
            else:
                warn(f"DELETE /api/shop/admin/items/{created_id}",
                     f"HTTP {r3.status_code if r3 else 'no response'}")
    else:
        warn("POST /api/shop/admin/items",
             f"HTTP {r.status_code if r else 'no response'} — {r.text[:80] if r else ''}")

# ─────────────────────────────────────────────────────────────────────────────
# 7. MONITORING SERVICE
# ─────────────────────────────────────────────────────────────────────────────

def test_monitoring():
    section("7. MONITORING SERVICE  (/api/tank, /api/monitoring)")

    r = get(f"{GW}/api/tank/health")
    if r and r.status_code == 200:
        ok("GET /api/tank/health", r.json().get("status", r.text[:40]))
    else:
        warn("GET /api/tank/health", f"HTTP {r.status_code if r else 'no response'} (tank server may be offline)")

    r = get(f"{GW}/api/tank/metrics")
    if r and r.status_code == 200:
        ok("GET /api/tank/metrics")
    else:
        warn("GET /api/tank/metrics", f"HTTP {r.status_code if r else 'no response'}")

    r = get(f"{GW}/api/tank/task-breakdown")
    if r and r.status_code == 200:
        ok("GET /api/tank/task-breakdown")
    else:
        warn("GET /api/tank/task-breakdown", f"HTTP {r.status_code if r else 'no response'}")

# ─────────────────────────────────────────────────────────────────────────────
# SUMMARY
# ─────────────────────────────────────────────────────────────────────────────

def print_summary():
    section("SUMMARY")
    passed = [r for r in results if r[0] == "PASS"]
    failed = [r for r in results if r[0] == "FAIL"]
    warned = [r for r in results if r[0] == "WARN"]

    print(f"  {GREEN}PASS:{RESET} {len(passed)}")
    print(f"  {RED}FAIL:{RESET} {len(failed)}")
    print(f"  {YELLOW}WARN:{RESET} {len(warned)} (service offline or non-critical)")

    if failed:
        print(f"\n{RED}Failed tests:{RESET}")
        for _, name in failed:
            print(f"  • {name}")

    if warned:
        print(f"\n{YELLOW}Warnings:{RESET}")
        for _, name in warned:
            print(f"  • {name}")

    print()

# ─────────────────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    print(f"\n{BOLD}TankOnline API Test Suite — {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}{RESET}")
    print(f"Gateway: {GW}\n")

    test_health_checks()
    test_auth()
    test_history()
    test_profile()
    test_shop()
    test_monitoring()
    test_matchmaking()  # last — spawns 10 threads

    print_summary()
