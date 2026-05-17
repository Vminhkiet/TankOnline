Act as a Senior DevOps Engineer and Grafana/Prometheus Expert.

I am preparing for my software engineering final defense. I need to create a highly professional Grafana setup to showcase the metrics of my "Tank Online" game backend, inspired by how AAA studios (like Riot Games) visualize their server performance.

My backend has two completely different ecosystems, and I want to split them into TWO separate Grafana Dashboards to present them clearly to the committee.

### PAGE 1: The C++ Game Server (High-Performance Metrics)
This page must ONLY show data for the C++ server. 
- CRITICAL FILTER: Every PromQL query here MUST strictly use `{job="tank-game-server"}` to isolate it from all other system containers.

I need the exact PromQL and step-by-step Grafana UI instructions (chart types, colors, overrides) for these 3 panels:
1. "The Budget Line" (Time Series): Plot `tank_tick_duration_us_avg` and `tank_tick_duration_us_p99`. 
   - CRITICAL UI TWEAK: Set the Y-axis Max to 18000 and add a hardcoded, highly visible RED threshold line at 16667 (my 60Hz tick budget) to visually emphasize the massive "Headroom" my server has.
2. "Stability Score / Overruns" (Stat Panel): Show the total increase of `tank_tick_overruns_total` over the selected time range. Format the text to show "X Overruns" and use thresholds (Green if < 5, Red if > 10).
3. "Load vs Latency" (Dual Axis Time Series): Plot `tank_active_matches` as Bars on the Right Y-axis, and `tank_tick_duration_us_p99` as a Line on the Left Y-axis.

### PAGE 2: The Java Microservices Ecosystem (System Health)
This page must ONLY monitor my Spring Boot services. 
- CRITICAL FILTER: You MUST NOT use a simple exclusion like `!=tank-game-server` because that will accidentally pull in infrastructure containers (Kafka, MySQL, Redis, cAdvisor). Instead, EVERY PromQL query here MUST strictly use a regex inclusion filter: `{job=~"eureka|gateway|auth|matchmaking|history|shop|monitoring"}`.

I need the exact PromQL and Grafana UI instructions for these 3 panels:
1. "Services Uptime" (Stat or State Timeline): Use the `up` metric with the regex filter to show the health status (1 = UP, 0 = DOWN) of the Java microservices, grouped by `job`.
2. "Global HTTP Throughput" (Time Series): Use `http_server_requests_seconds_count` with the regex filter to calculate the total requests per second (RPS) across all Java services.
3. "JVM Heap Memory Usage" (Time Series): Plot `jvm_memory_used_bytes{area="heap"}` with the regex filter and convert it to Megabytes.

Please output the response clearly, divided into "DASHBOARD 1" and "DASHBOARD 2", with the exact PromQL code blocks and precise bullet points for the Grafana UI settings.