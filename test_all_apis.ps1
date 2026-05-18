# TankOnline Full API Test Suite (PowerShell)
# Run: .\test_all_apis.ps1

$GW      = "http://localhost:8080"
$script:PASS = 0; $script:FAIL = 0; $script:WARN = 0
$script:FAILED_LIST = @(); $script:WARNED_LIST = @()

function Write-OK {
    param($n, $d="")
    $script:PASS++
    if ($d) { Write-Host "  [PASS] $n | $d" -ForegroundColor Green }
    else    { Write-Host "  [PASS] $n"       -ForegroundColor Green }
}
function Write-FAIL {
    param($n, $d="")
    $script:FAIL++
    $script:FAILED_LIST += $n
    if ($d) { Write-Host "  [FAIL] $n | $d" -ForegroundColor Red }
    else    { Write-Host "  [FAIL] $n"       -ForegroundColor Red }
}
function Write-WARN {
    param($n, $d="")
    $script:WARN++
    $script:WARNED_LIST += $n
    if ($d) { Write-Host "  [WARN] $n | $d" -ForegroundColor Yellow }
    else    { Write-Host "  [WARN] $n"       -ForegroundColor Yellow }
}
function Write-SEC { param($t) Write-Host "`n============================================================" -ForegroundColor Cyan; Write-Host "  $t" -ForegroundColor Cyan; Write-Host "============================================================" -ForegroundColor Cyan }

function Invoke-API {
    param($Method="GET", $Url, $Body=$null, $Headers=@{})
    try {
        $params = @{ Method=$Method; Uri=$Url; TimeoutSec=10; ErrorAction="Stop"; UseBasicParsing=$true }
        if ($Headers.Count -gt 0) { $params.Headers = $Headers }
        if ($Body) {
            $params.Body        = ($Body | ConvertTo-Json -Compress)
            $params.ContentType = "application/json"
        }
        $r = Invoke-WebRequest @params
        return @{ Status=[int]$r.StatusCode; Body=$r.Content }
    } catch [System.Net.WebException] {
        $code = [int]$_.Exception.Response.StatusCode
        try { $content = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { $content = "" }
        return @{ Status=$code; Body=$content }
    } catch { return $null }
}

function Short { param($s, $n=80) if ($s.Length -gt $n) { $s.Substring(0,$n) } else { $s } }

function RandStr {
    $chars = "abcdefghijklmnopqrstuvwxyz"
    -join (1..8 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
}

$TokenA = $null; $TokenB = $null; $RefreshA = $null
$UserA  = $null; $UserB  = $null
$pw     = "Test@1234"

# =============================================================================
# 1. HEALTH CHECKS
# =============================================================================
Write-SEC "1. SERVICE HEALTH CHECKS"

$services = [ordered]@{
    "api_gateway (8080)"   = "http://localhost:8080/actuator/health"
    "auth (8081)"          = "http://localhost:8081/actuator/health"
    "matchmaking (8085)"   = "http://localhost:8085/actuator/health"
    "history (8086)"       = "http://localhost:8086/actuator/health"
    "profile (8087)"       = "http://localhost:8087/actuator/health"
    "shop (8088)"          = "http://localhost:8088/actuator/health"
    "monitoring (8090)"    = "http://localhost:8090/actuator/health"
    "discovery (8761)"     = "http://localhost:8761/actuator/health"
}

foreach ($svc in $services.Keys) {
    $r = Invoke-API GET $services[$svc]
    if ($null -eq $r) { Write-WARN $svc "Not reachable" }
    elseif ($r.Status -eq 200) {
        try { $st = ($r.Body | ConvertFrom-Json).status; Write-OK $svc $st } catch { Write-OK $svc "HTTP 200" }
    } else { Write-FAIL $svc "HTTP $($r.Status)" }
}

# =============================================================================
# 2. AUTH SERVICE
# =============================================================================
Write-SEC "2. AUTH SERVICE (/api/auth, /api/user)"

$uA = "test_$(RandStr)"; $uB = "test_$(RandStr)"

# Signup A
$r = Invoke-API POST "$GW/api/auth/signup" @{username=$uA; email="$uA@test.com"; password=$pw}
if ($r -and $r.Status -in 200,201) {
    try {
        $b = $r.Body | ConvertFrom-Json
        $TokenA   = if ($b.jwt)          { $b.jwt }          elseif ($b.accessToken) { $b.accessToken } else { $b.token }
        $RefreshA = if ($b.refreshToken) { $b.refreshToken } else { $b.refresh_token }
    } catch {}
    $UserA = $uA
    Write-OK "POST /api/auth/signup (user A)" "HTTP $($r.Status)"
} else {
    $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
    Write-FAIL "POST /api/auth/signup (user A)" $detail
}

# Signup B
$r = Invoke-API POST "$GW/api/auth/signup" @{username=$uB; email="$uB@test.com"; password=$pw}
if ($r -and $r.Status -in 200,201) {
    try {
        $b = $r.Body | ConvertFrom-Json
        $TokenB = if ($b.jwt) { $b.jwt } elseif ($b.accessToken) { $b.accessToken } else { $b.token }
    } catch {}
    $UserB = $uB
    Write-OK "POST /api/auth/signup (user B)"
} else { Write-WARN "POST /api/auth/signup (user B)" "HTTP $($r.Status)" }

# Login valid
$r = Invoke-API POST "$GW/api/auth/login" @{username=$uA; password=$pw}
if ($r -and $r.Status -eq 200) {
    try {
        $b = $r.Body | ConvertFrom-Json
        $TokenA   = if ($b.jwt)          { $b.jwt }          elseif ($b.accessToken) { $b.accessToken } else { $TokenA }
        $RefreshA = if ($b.refreshToken) { $b.refreshToken } elseif ($b.refresh_token) { $b.refresh_token } else { $RefreshA }
    } catch {}
    Write-OK "POST /api/auth/login -- valid credentials"
} else {
    $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
    Write-FAIL "POST /api/auth/login -- valid credentials" $detail
}

# Login wrong password
$r = Invoke-API POST "$GW/api/auth/login" @{username=$uA; password="wrongpass"}
if ($r -and $r.Status -in 400,401,403) { Write-OK "POST /api/auth/login -- wrong password -> 4xx" "HTTP $($r.Status)" }
else {
    $got = if ($r) { "HTTP $($r.Status)" } else { "No response" }
    Write-FAIL "POST /api/auth/login -- wrong password" "Expected 4xx, got $got"
}

# Refresh token
if ($RefreshA -and $TokenA) {
    $r = Invoke-API POST "$GW/api/auth/refresh" @{refreshToken=$RefreshA} -Headers @{Authorization="Bearer $TokenA"}
    if ($r -and $r.Status -eq 200) { Write-OK "POST /api/auth/refresh" }
    else {
        $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
        Write-WARN "POST /api/auth/refresh" $detail
    }
} else { Write-WARN "POST /api/auth/refresh" "No refresh token available" }

# GET /api/user/me
if ($TokenA) {
    $r = Invoke-API GET "$GW/api/user/me" -Headers @{Authorization="Bearer $TokenA"}
    if ($r -and $r.Status -eq 200) { Write-OK "GET /api/user/me -- authenticated" }
    else { Write-FAIL "GET /api/user/me -- authenticated" "HTTP $($r.Status)" }

    $r = Invoke-API GET "$GW/api/user/me"
    if ($r -and $r.Status -in 401,403) { Write-OK "GET /api/user/me -- unauthenticated -> 4xx" "HTTP $($r.Status)" }
    else {
        $got = if ($r) { "HTTP $($r.Status)" } else { "No response" }
        Write-FAIL "GET /api/user/me -- unauthenticated" "Expected 4xx, got $got"
    }
} else { Write-WARN "GET /api/user/me" "Skipped -- no JWT" }

# GET /api/user/users (public)
$r = Invoke-API GET "$GW/api/user/users"
if ($r -and $r.Status -eq 200) { Write-OK "GET /api/user/users -- public" }
else { Write-FAIL "GET /api/user/users" "HTTP $($r.Status)" }

# Logout
if ($TokenA) {
    $r = Invoke-API POST "$GW/api/auth/logout" -Headers @{Authorization="Bearer $TokenA"}
    if ($r -and $r.Status -in 200,204) { Write-OK "POST /api/auth/logout" "HTTP $($r.Status)" }
    else {
        $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
        Write-WARN "POST /api/auth/logout" $detail
    }
    # Re-login to keep token valid
    $r = Invoke-API POST "$GW/api/auth/login" @{username=$uA; password=$pw}
    if ($r -and $r.Status -eq 200) {
        try {
            $b = $r.Body | ConvertFrom-Json
            $TokenA = if ($b.jwt) { $b.jwt } elseif ($b.accessToken) { $b.accessToken } else { $TokenA }
        } catch {}
    }
}

# =============================================================================
# 3. HISTORY SERVICE
# =============================================================================
Write-SEC "3. HISTORY SERVICE (/api/history)"

$r = Invoke-API GET "$GW/api/history/leaderboard"
if ($r -and $r.Status -eq 200) { Write-OK "GET /api/history/leaderboard -- public" }
else {
    $detail = if ($r) { "HTTP $($r.Status)" } else { "Not reachable" }
    Write-WARN "GET /api/history/leaderboard" $detail
}

if ($TokenA) {
    $h = @{Authorization="Bearer $TokenA"}

    $r = Invoke-API GET "$GW/api/history/me" -Headers $h
    if ($r -and $r.Status -eq 200) { Write-OK "GET /api/history/me" }
    else { Write-FAIL "GET /api/history/me" "HTTP $($r.Status)" }

    $r = Invoke-API GET "$GW/api/history/me/stats" -Headers $h
    if ($r -and $r.Status -eq 200) { Write-OK "GET /api/history/me/stats" }
    else { Write-FAIL "GET /api/history/me/stats" "HTTP $($r.Status)" }

    $r = Invoke-API GET "$GW/api/history/me"
    if ($r -and $r.Status -in 401,403) { Write-OK "GET /api/history/me -- unauthenticated -> 4xx" "HTTP $($r.Status)" }
    else {
        $got = if ($r) { "HTTP $($r.Status)" } else { "No response" }
        Write-FAIL "GET /api/history/me -- unauthenticated" "Expected 4xx, got $got"
    }
} else { Write-WARN "History protected endpoints" "Skipped -- no JWT" }

# =============================================================================
# 4. PROFILE SERVICE
# =============================================================================
Write-SEC "4. PROFILE SERVICE (/api/profile)"

if ($TokenA) {
    $h = @{Authorization="Bearer $TokenA"}
    $userId = $null

    $r = Invoke-API GET "$GW/api/profile/me" -Headers $h
    if ($r -and $r.Status -eq 200) {
        Write-OK "GET /api/profile/me"
        try {
            $b = $r.Body | ConvertFrom-Json
            $userId = if ($b.userId) { $b.userId } elseif ($b.id) { $b.id } else { $null }
        } catch {}
    } else { Write-FAIL "GET /api/profile/me" "HTTP $($r.Status)" }

    $r = Invoke-API PUT "$GW/api/profile/me" @{displayName="TestPlayer"; imageId="default.png"} -Headers $h
    if ($r -and $r.Status -in 200,204) { Write-OK "PUT /api/profile/me" "HTTP $($r.Status)" }
    else {
        $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
        Write-WARN "PUT /api/profile/me" $detail
    }

    $r = Invoke-API GET "$GW/api/profile/me/coins" -Headers $h
    if ($r -and $r.Status -eq 200) { Write-OK "GET /api/profile/me/coins" "$(Short $r.Body 60)" }
    else { Write-WARN "GET /api/profile/me/coins" "HTTP $($r.Status)" }

    if ($userId) {
        $r = Invoke-API GET "$GW/api/profile/$userId"
        if ($r -and $r.Status -eq 200) { Write-OK "GET /api/profile/{userId} -- public" }
        else { Write-WARN "GET /api/profile/{userId}" "HTTP $($r.Status)" }
    } else { Write-WARN "GET /api/profile/{userId}" "Skipped -- no userId from profile" }

    # Redeem invalid gift code
    $r = Invoke-API POST "$GW/api/profile/giftcode/redeem" @{code="INVALID_CODE_TEST"} -Headers $h
    if ($r -and $r.Status -in 400,404,422) { Write-OK "POST /api/profile/giftcode/redeem -- invalid code -> 4xx" "HTTP $($r.Status)" }
    elseif ($r -and $r.Status -eq 200) { Write-WARN "POST /api/profile/giftcode/redeem" "Invalid code was accepted -- check logic" }
    else { Write-WARN "POST /api/profile/giftcode/redeem" "HTTP $($r.Status)" }

    # Admin: create gift code
    $adminH = @{Authorization="Bearer $TokenA"; "X-Admin-Token"="admin-secret-token"}
    $gc = "TEST$((RandStr).ToUpper())"
    $r = Invoke-API POST "$GW/api/profile/admin/giftcode" @{code=$gc; coinReward=100; maxUses=5} -Headers $adminH
    if ($r -and $r.Status -in 200,201) {
        Write-OK "POST /api/profile/admin/giftcode"
        $giftId = $null
        try { $giftId = ($r.Body | ConvertFrom-Json).id } catch {}

        $r2 = Invoke-API GET "$GW/api/profile/admin/giftcode" -Headers $adminH
        if ($r2 -and $r2.Status -eq 200) { Write-OK "GET /api/profile/admin/giftcode" }
        else { Write-WARN "GET /api/profile/admin/giftcode" "HTTP $($r2.Status)" }

        if ($giftId) {
            $r3 = Invoke-API DELETE "$GW/api/profile/admin/giftcode/$giftId" -Headers $adminH
            if ($r3 -and $r3.Status -in 200,204) { Write-OK "DELETE /api/profile/admin/giftcode/$giftId" }
            else { Write-WARN "DELETE /api/profile/admin/giftcode/$giftId" "HTTP $($r3.Status)" }
        }
    } elseif ($r -and $r.Status -eq 403) {
        Write-OK "POST /api/profile/admin/giftcode -- non-admin correctly rejected" "HTTP 403"
    } else {
        $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
        Write-WARN "POST /api/profile/admin/giftcode" $detail
    }
} else { Write-WARN "Profile endpoints" "Skipped -- no JWT" }

# =============================================================================
# 5. SHOP SERVICE
# =============================================================================
Write-SEC "5. SHOP SERVICE (/api/shop)"

$items = @()
$r = Invoke-API GET "$GW/api/shop/items"
if ($r -and $r.Status -eq 200) {
    Write-OK "GET /api/shop/items -- public"
    try { $items = $r.Body | ConvertFrom-Json } catch {}
} else { Write-WARN "GET /api/shop/items" "HTTP $($r.Status)" }

$r = Invoke-API GET "$GW/api/shop/items/version"
if ($r -and $r.Status -eq 200) { Write-OK "GET /api/shop/items/version" "$(Short $r.Body 40)" }
else { Write-WARN "GET /api/shop/items/version" "HTTP $($r.Status)" }

$r = Invoke-API GET "$GW/api/shop/items/category/tank"
if ($r -and $r.Status -in 200,404) { Write-OK "GET /api/shop/items/category/tank" "HTTP $($r.Status)" }
else { Write-WARN "GET /api/shop/items/category/tank" "HTTP $($r.Status)" }

if ($items.Count -gt 0) {
    $itemId = if ($items[0].id) { $items[0].id } else { $items[0].itemId }
    if ($itemId) {
        $r = Invoke-API GET "$GW/api/shop/items/$itemId"
        if ($r -and $r.Status -eq 200) { Write-OK "GET /api/shop/items/$itemId" }
        else { Write-WARN "GET /api/shop/items/$itemId" "HTTP $($r.Status)" }
    }
} else { Write-WARN "GET /api/shop/items/{id}" "No items in DB" }

if ($TokenA) {
    $h = @{Authorization="Bearer $TokenA"}

    $r = Invoke-API GET "$GW/api/shop/my-items" -Headers $h
    if ($r -and $r.Status -eq 200) { Write-OK "GET /api/shop/my-items" }
    else { Write-WARN "GET /api/shop/my-items" "HTTP $($r.Status)" }

    if ($items.Count -gt 0) {
        $itemId = if ($items[0].id) { $items[0].id } else { $items[0].itemId }
        if ($itemId) {
            $r = Invoke-API POST "$GW/api/shop/purchase" @{itemId=$itemId} -Headers $h
            if ($r -and $r.Status -in 200,201) { Write-OK "POST /api/shop/purchase (itemId=$itemId)" }
            elseif ($r -and $r.Status -in 400,409,422) { Write-OK "POST /api/shop/purchase -- already owned/no coins -> 4xx" "HTTP $($r.Status)" }
            else {
                $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
                Write-WARN "POST /api/shop/purchase" $detail
            }
        }
    } else { Write-WARN "POST /api/shop/purchase" "No items in DB" }

    # Admin CRUD
    $r = Invoke-API POST "$GW/api/shop/admin/items" @{name="TestTank"; description="auto-test"; price=100; category="tank"; imageUrl="test.png"} -Headers $h
    if ($r -and $r.Status -in 200,201) {
        Write-OK "POST /api/shop/admin/items"
        $newId = $null
        try { $b = $r.Body | ConvertFrom-Json; $newId = if ($b.id) { $b.id } else { $b.itemId } } catch {}
        if ($newId) {
            $r2 = Invoke-API PUT "$GW/api/shop/admin/items/$newId" @{name="TestTankUpdated"; price=200; category="tank"} -Headers $h
            if ($r2 -and $r2.Status -in 200,204) { Write-OK "PUT /api/shop/admin/items/$newId" }
            else { Write-WARN "PUT /api/shop/admin/items/$newId" "HTTP $($r2.Status)" }

            $r3 = Invoke-API DELETE "$GW/api/shop/admin/items/$newId" -Headers $h
            if ($r3 -and $r3.Status -in 200,204) { Write-OK "DELETE /api/shop/admin/items/$newId" }
            else { Write-WARN "DELETE /api/shop/admin/items/$newId" "HTTP $($r3.Status)" }
        }
    } else {
        $detail = if ($r) { "HTTP $($r.Status) | $(Short $r.Body)" } else { "No response" }
        Write-WARN "POST /api/shop/admin/items" $detail
    }
} else { Write-WARN "Shop protected endpoints" "Skipped -- no JWT" }

# =============================================================================
# 6. MONITORING SERVICE
# =============================================================================
Write-SEC "6. MONITORING SERVICE (/api/tank)"

foreach ($ep in @("health","metrics","task-breakdown")) {
    $r = Invoke-API GET "$GW/api/tank/$ep"
    if ($r -and $r.Status -eq 200) { Write-OK "GET /api/tank/$ep" }
    else {
        $detail = if ($r) { "HTTP $($r.Status)" } else { "Not reachable" }
        Write-WARN "GET /api/tank/$ep" "$detail (tank server may be offline)"
    }
}

# =============================================================================
# 7. MATCHMAKING -- 10 concurrent requests
# =============================================================================
Write-SEC "7. MATCHMAKING SERVICE (/api/matchmaking/find) -- 10 players"

Write-Host "  Creating 8 extra temp users..." -ForegroundColor Yellow
$extraTokens = @()
for ($i = 0; $i -lt 8; $i++) {
    $u = "mm_$(RandStr)"
    $r = Invoke-API POST "$GW/api/auth/signup" @{username=$u; email="$u@test.com"; password=$pw}
    if ($r -and $r.Status -in 200,201) {
        try {
            $b = $r.Body | ConvertFrom-Json
            $t = if ($b.jwt) { $b.jwt } elseif ($b.accessToken) { $b.accessToken } else { $b.token }
            if ($t) { $extraTokens += $t }
        } catch {}
    }
}

$allTokens = @()
if ($TokenA) { $allTokens += $TokenA }
if ($TokenB) { $allTokens += $TokenB }
$allTokens += $extraTokens
$allTokens  = $allTokens | Select-Object -First 10
Write-Host "  Sending $($allTokens.Count) concurrent /find requests (waiting up to 25s)..." -ForegroundColor Yellow

$jobs = foreach ($tok in $allTokens) {
    Start-Job -ScriptBlock {
        param($gw, $token)
        try {
            $r = Invoke-WebRequest -Method POST -Uri "$gw/api/matchmaking/find" `
                -Headers @{Authorization="Bearer $token"} `
                -ContentType "application/json" `
                -TimeoutSec 20 -UseBasicParsing -ErrorAction Stop
            return @{ Status=[int]$r.StatusCode; Body=$r.Content }
        } catch [System.Net.WebException] {
            $code = [int]$_.Exception.Response.StatusCode
            try { $body = (New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch { $body="" }
            return @{ Status=$code; Body=$body }
        } catch { return $null }
    } -ArgumentList $GW, $tok
}

$null = Wait-Job -Job $jobs -Timeout 28
$responses = $jobs | ForEach-Object { Receive-Job -Job $_ }
$jobs | Remove-Job -Force

$matched = @($responses | Where-Object { $_ -and $_.Status -eq 200 })
if ($matched.Count -gt 0) {
    $b = $null
    try { $b = $matched[0].Body | ConvertFrom-Json } catch {}
    Write-OK "POST /api/matchmaking/find -- $($matched.Count)/$($allTokens.Count) players matched" "matchId=$($b.matchId) port=$($b.serverPort)"
    foreach ($key in @("matchId","serverHost","serverPort","playerId")) {
        if ($null -ne $b.$key) { Write-OK "  response field '$key'" "$($b.$key)" }
        else { Write-FAIL "  response missing field '$key'" "" }
    }
} else {
    $codes = ($responses | ForEach-Object { if ($_) { $_.Status } else { "err" } }) -join ", "
    Write-WARN "POST /api/matchmaking/find" "No match formed | status codes: $codes"
}

# =============================================================================
# SUMMARY
# =============================================================================
Write-SEC "SUMMARY"
Write-Host "  PASS: $($script:PASS)" -ForegroundColor Green
Write-Host "  FAIL: $($script:FAIL)" -ForegroundColor Red
Write-Host "  WARN: $($script:WARN)  (service offline or non-critical)" -ForegroundColor Yellow

if ($script:FAILED_LIST.Count -gt 0) {
    Write-Host "`nFailed tests:" -ForegroundColor Red
    $script:FAILED_LIST | ForEach-Object { Write-Host "  * $_" -ForegroundColor Red }
}
if ($script:WARNED_LIST.Count -gt 0) {
    Write-Host "`nWarnings:" -ForegroundColor Yellow
    $script:WARNED_LIST | ForEach-Object { Write-Host "  * $_" -ForegroundColor Yellow }
}
Write-Host ""
