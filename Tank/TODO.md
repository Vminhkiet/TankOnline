# TODO - Duplicate Login Force Logout (Production Path)

- [x] 1) Inspect C++ server_tank protocol/network files to locate:
  - opcode definitions
  - packet serialization path for S2C raw packets
  - session mapping of player/user to UDP endpoint

- [ ] 2) Add new S2C opcode for force logout in C++ and Unity protocol:
  - `S2C_FORCE_LOGOUT = 2001`

- [ ] 3) Implement C++ server support for force logout packet:
  - packet format with `code=1003`, message, timestamp
  - send to target online user

- [ ] 4) Implement online user mapping in server runtime:
  - ensure `userId -> session/endpoint` lookup exists for kick routing

- [ ] 5) Add Kafka consumer bridge (or equivalent bridge module) for topic:
  - `user.session.invalidated`
  - parse payload `{ userId, code, message, timestamp }`
  - trigger force logout for that user

- [ ] 6) Unity client network integration:
  - parse opcode 2001
  - invoke existing force logout UX flow (`AuthSessionRuntime`)

- [ ] 7) Critical-path validation:
  - duplicate login triggers warning in old session then logout
  - non-online user event is safely ignored
  - app-exit logout behavior still works
