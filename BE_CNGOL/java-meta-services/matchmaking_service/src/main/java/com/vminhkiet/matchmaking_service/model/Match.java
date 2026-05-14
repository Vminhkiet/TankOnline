package com.vminhkiet.matchmaking_service.model;

import lombok.AllArgsConstructor;
import lombok.Data;
import lombok.NoArgsConstructor;

import java.util.List;
import java.time.Instant;

@Data
@NoArgsConstructor
@AllArgsConstructor
public class Match {
    private String matchId;
    private List<String> players;
    private Instant createAt;
}
