Act as a Senior Performance QA Engineer.

I have successfully refactored my C++ game server into a "Clean Room" environment. Disk I/O bottlenecks are removed, and my `Match::tick` now accurately measures pure CPU time for `updateBullets` and `detectCollisions` by aggregating over 600 ticks. 

However, my previous baseline test only ran with 2 players per match. The swept-sphere collision cost was negligible (4µs). To find my actual Games Per Core (GPC) limit before hitting the 16.6ms budget, I need to trigger the O(B * T) physics bottleneck.

Please provide the exact Python code modifications for my stress testing script (`tank_stress_match.py`) to implement the following "Heavy Workload" scenario:

### 1. 10 Players Per Match
Ensure the matchmaking or match-creation logic spawns exactly 10 players per match (the maximum capacity). 

### 2. Trigger-Happy Bots (1.5 shots/second)
Modify the main loop (`KeepAlive._loop`) so that EACH of the 10 players fires a bullet exactly 1.5 times per second. 
- Movement packets (Opcode `1001`) are sent at 15Hz.
- Interleave the Shoot packets (Opcode `1002`) seamlessly without blocking the thread. Use the `_BW` (BitWriter) class for the 12-byte payload.

### 3. Ramp-Up Strategy
Provide a clean way (either via command-line arguments `argparse` or a simple loop delay) to ramp up the number of matches dynamically. 
I want to inject 5 matches (50 bots), wait for a few minutes to observe Grafana, then inject 5 more, continuing this staircase pattern until the server's `tick_max` exceeds 16,667µs.

Please output the refactored Python methods clearly so I can plug them directly into my script and start the final benchmark.