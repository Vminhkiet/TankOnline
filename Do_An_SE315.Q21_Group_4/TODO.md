# TODO - Unity client force logout flow

- [x] Add a client-side force logout manager to show warning countdown (10s) and then leave game.
- [x] Hook automatic logout call on app quit (best-effort, short timeout).
- [x] Add simple API in AuthenticationUIManager to clear local auth and return to login scene.
- [x] Integrate with existing network flow (manual trigger method now; wire realtime packet later from gateway message handler).
- [ ] Critical-path test:
  - [ ] Trigger force logout manually in play mode and verify warning + countdown + return to login.
  - [ ] Verify OnApplicationQuit calls logout endpoint best-effort and clears local token.

## Progress
- [x] Plan approved by user
- [x] Refactored to singleton persistent runtime (AuthSessionRuntime)
