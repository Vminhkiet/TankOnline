#!/bin/bash

echo "[1/2] Stopping Java services..."
ps aux | grep "java -jar" | grep -v grep | awk '{print $2}' | xargs -r kill -9 2>/dev/null || true
echo "      Java services stopped"

echo "[2/2] Stopping Docker services..."
cd /home/minhk/project/SE315.Q21/BE_CNGOL && docker-compose stop
echo "      Docker stopped"

echo ""
echo "All services stopped. (Tank server: close manually on Windows)"
