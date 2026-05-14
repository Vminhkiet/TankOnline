package com.vminhkiet.matchmaking_service.service;

import com.vminhkiet.matchmaking_service.model.Player;
import com.vminhkiet.matchmaking_service.model.Match;
import java.util.List;

public interface MatchMakingService {
    void enqueuePlayer(Player player);
    Player dequeuePlayer();
    Match createMatch(List<String> players);
}
