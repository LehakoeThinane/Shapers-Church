#!/bin/bash
# Runs on the demo VM at every boot (Google Compute Engine startup script). Idempotent.
# The free e2-micro has 1 GB of memory: a 2 GB swap file keeps the API from running out while it starts.
set -euo pipefail

if [ ! -f /swapfile ]; then
  fallocate -l 2G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile
  echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi
swapon -a || true

if ! command -v docker >/dev/null 2>&1; then
  apt-get update -y
  apt-get install -y docker.io docker-compose-v2
  systemctl enable --now docker
fi

# Security updates install themselves.
if ! dpkg -s unattended-upgrades >/dev/null 2>&1; then
  apt-get install -y unattended-upgrades
fi

mkdir -p /opt/shapers
