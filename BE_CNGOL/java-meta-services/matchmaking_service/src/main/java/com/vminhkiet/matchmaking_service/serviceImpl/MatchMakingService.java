package com.vminhkiet.matchmaking_service.serviceImpl;


import java.time.Instant;
import java.util.List;
import java.util.UUID;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.stereotype.Service;

import com.vminhkiet.matchmaking_service.model.Match;
import com.vminhkiet.matchmaking_service.model.Player;


@Service
public class MatchMakingService implements com.vminhkiet.matchmaking_service.service.MatchMakingService{
    @Autowired
    private RedisTemplate<String, Object> redisTemplate;
    private static final String QUEUE_KEY = "matchmaking:queue";

    @Override
    public void enqueuePlayer(Player player) {
        redisTemplate.opsForList().rightPush(QUEUE_KEY, player);
        redisTemplate.opsForValue().set("matchmaking:player:" + player.getId() + ":status", "waiting");
    }

    @Override
    public Player dequeuePlayer() {
        Object obj = redisTemplate.opsForList().leftPop(QUEUE_KEY);
        return obj != null ? (Player) obj : null;
    }

    @Override
    public Match createMatch(List<String> players) {
        String matchId = UUID.randomUUID().toString();
        Match match = new Match(matchId, players, Instant.now());

        redisTemplate.opsForHash().put("matchmaking:match", matchId, match);

        players.forEach(id -> redisTemplate.opsForValue().set(
            "matchmaking:player:" + id + ":status", 
            "matched"
        ));
        return match;
    }
}
