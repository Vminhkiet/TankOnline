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
    private long         matchId;     // numeric uint32-compatible — dùng cho Tank C++ và Unity
    private List<String> players;
    private Instant      createAt;
    private String       serverHost;  // UDP host của Tank server, trả về cho Unity
    private int          serverPort;  // UDP port của Tank server, trả về cho Unity
}
