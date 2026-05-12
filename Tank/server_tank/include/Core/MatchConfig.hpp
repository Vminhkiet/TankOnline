#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include <unordered_map>

struct MatchConfig {
    uint32_t             matchId        = 0;
    std::vector<uint32_t> playerIds;          // expected player IDs (pre-auth)
    std::string          mapName        = "world";
    int                  maxDurationSecs = 300;  // 5 min default
    int                  port           = 8080;
};

enum class MatchOutcome { Running, Win, Draw, Timeout };

struct MatchResult {
    uint32_t     matchId     = 0;
    MatchOutcome outcome     = MatchOutcome::Draw;
    uint32_t     winnerId    = 0;   // valid when outcome == Win
    float        durationSecs = 0.f;
    std::unordered_map<uint32_t, int> kills; // playerId → kill count
};
