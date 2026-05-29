#pragma once
#include <cstdint>
#include <string>
#include <vector>
#include <unordered_map>
struct TankStats {
    std::string name = "BULLDOG";
    int damage = 25;
    int armor = 0;
    float speed = 12.f;
    int health = 100;
    float fireRate = 1.0f;
    float fireRange = 50.f;
    bool canMoveWhileShooting = true;
    bool holdsToCharge = false; // New toggle for charging mechanic
    int barrelCount = 1;        // Number of barrels (default 1)
    float barrelSpacing = 0.4f; // Lateral distance between barrels
    int magazineCapacity = 1;   // Default 1 shot per magazine
    float reloadTime = 2.0f;    // Default 2 seconds reload time
};

struct MatchConfig {
    uint32_t             matchId        = 0;
    std::vector<uint32_t> playerIds;
    std::unordered_map<uint32_t, std::string> userIds;   // playerId -> userId string
    std::unordered_map<uint32_t, std::string> playerTokens; // playerId -> match token
    std::unordered_map<uint32_t, TankStats>   playerStats; // playerId -> tank stats
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
    std::unordered_map<uint32_t, int>         placements; // playerId -> placement
    std::unordered_map<uint32_t, int>         matchScores; // playerId -> score
    std::unordered_map<uint32_t, int>         rpRewards; // playerId -> RP reward
    std::unordered_map<uint32_t, int>         damageDealt; // playerId -> damage
    std::unordered_map<uint32_t, std::string> userIds; // playerId -> userId string
    std::string  mapName     = "world";
};
