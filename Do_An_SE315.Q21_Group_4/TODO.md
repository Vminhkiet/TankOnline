# TODO - Leaderboard Top 10 (Backend + Client UI)

- [x] Backend: Extend `/api/history/leaderboard` response to include `username` (along with `playerId`, `totalKills`, `totalMatches`, `wins`).
- [x] Backend: Ensure leaderboard ordering remains by `totalKills DESC` and returns top 10.
- [x] Client: Add `LeaderboardUIManager` to call `/api/history/leaderboard`.
- [x] Client: Bind top 1..4 into 4 dedicated prefabs (unique visuals).
- [x] Client: Bind ranks 5..10 into shared row prefab/list container.
- [x] Client: Map and display fields on UI: `rank`, `username`, `totalKills`, `totalMatches`, `wins`.
- [ ] Run API critical-path verification and report testing status/remaining coverage.
