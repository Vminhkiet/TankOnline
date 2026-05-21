#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include <unordered_map>

struct MatchConfig {
    uint32_t             matchId        = 0;
    std::vector<uint32_t> playerIds;
    std::unordered_map<uint32_t, std::string> userIds;   // playerId -> userId string
    std::unordered_map<uint32_t, std::string> playerTokens; // playerId -> match token
    std::string          mapName        = "world";
    int                  maxDurationSecs = 300;
    int                  port           = 8080;
};

enum class MatchOutcome { Running, Win, Draw, Timeout };

struct MatchResult {
    uint32_t     matchId     = 0;
    MatchOutcome outcome     = MatchOutcome::Draw;
    uint32_t     winnerId    = 0;
    float        durationSecs = 0.f;
    std::unordered_map<uint32_t, int>         kills;   // playerId -> kill count
    std::unordered_map<uint32_t, int>         deaths;  // playerId -> death count
    std::unordered_map<uint32_t, std::string> userIds; // playerId -> userId string
    std::string  mapName     = "world";
};
