# TODO - Battle History Card (Unity)

- [x] Analyze existing `MatchHistoryUIManager` data flow (`/api/history/me`, prefab instantiate).
- [x] Confirm sprite source: `Assets/Resources/Sprites/UI` with sub-sprites `Win`, `Lose`, `Draw`.
- [x] Update `MatchHistoryUIManager.cs` to:
  - [x] Load and cache result sprites from `Resources/Sprites/UI`.
  - [x] Map result values (`WIN`/`LOSE`/`DRAW`) to `Win`/`Lose`/`Draw`.
  - [x] Bind result sprite to card image (`ResultImage`) while keeping text fallback.
  - [x] Populate `playedAt` text when available.
  - [x] Ensure list is instantiated under `historyContainer` (Scroll View Content).
- [x] Validate Scroll View setup assumptions in code comments/logs.
- [ ] Testing:
  - [ ] Critical-path: history cards render in Scroll View with correct result sprite.
  - [ ] Thorough: edge cases (missing sprite, lowercase result, missing playedAt).
