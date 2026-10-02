#!/usr/bin/env sh
# Serves the MultiTravel leaderboard folder on the local network (static files only).
# Usage: ./serve.sh [port]      (default port 8080; stop with Ctrl+C)
# Uses `npx serve` when Node.js is available, otherwise `python -m http.server`.
set -eu

PORT="${1:-8080}"
cd "$(dirname "$0")"

echo ""
echo "MultiTravel Valiz Challenge - Liderlik Tablosu"
echo "Bu bilgisayarda : http://localhost:${PORT}/"

# Best-effort LAN address discovery (Linux: hostname -I, macOS: ipconfig, fallback: ip/ifconfig).
addresses=""
if command -v hostname >/dev/null 2>&1; then
  addresses="$(hostname -I 2>/dev/null || true)"
fi
if [ -z "$addresses" ] && command -v ipconfig >/dev/null 2>&1; then
  for iface in en0 en1; do
    a="$(ipconfig getifaddr "$iface" 2>/dev/null || true)"
    [ -n "$a" ] && addresses="$addresses $a"
  done
fi
if [ -z "$addresses" ] && command -v ip >/dev/null 2>&1; then
  addresses="$(ip -4 -o addr show scope global 2>/dev/null | awk '{print $4}' | cut -d/ -f1 | tr '\n' ' ')"
fi
for ip in $addresses; do
  echo "Ağdaki cihazlar : http://${ip}:${PORT}/"
done
echo "Belirli bir etkinlik için: ...?event=<etkinlik-slug>"
echo "Durdurmak için Ctrl+C."
echo ""

if command -v npx >/dev/null 2>&1; then
  exec npx --yes serve -l "$PORT" --no-clipboard .
elif command -v python3 >/dev/null 2>&1; then
  exec python3 -m http.server "$PORT" --bind 0.0.0.0
elif command -v python >/dev/null 2>&1; then
  exec python -m http.server "$PORT" --bind 0.0.0.0
else
  echo "Node.js (npx) veya Python bulunamadı. https://nodejs.org veya https://python.org adresinden birini kurun." >&2
  exit 1
fi
