#!/bin/sh
# Publishes registration-web/ to Vercel (project multitravel-kayit) with the real client config.
# Needs: Vercel CLI logged in, Deployment/registration.config.js (git-ignored; Supabase URL + anon key + event slug).
set -e
ROOT=$(cd "$(dirname "$0")/.." && pwd)
CONFIG="$ROOT/Deployment/registration.config.js"
[ -f "$CONFIG" ] || { echo "Missing $CONFIG" >&2; exit 1; }
DIST=$(mktemp -d)
trap 'rm -rf "$DIST"' EXIT
cp -R "$ROOT/registration-web/." "$DIST/"
rm -f "$DIST/README.md"
cp "$CONFIG" "$DIST/config.js"
cat > "$DIST/vercel.json" <<'JSON'
{ "headers": [ { "source": "/(.*)", "headers": [
  { "key": "X-Robots-Tag", "value": "noindex, nofollow" },
  { "key": "Referrer-Policy", "value": "no-referrer" },
  { "key": "X-Content-Type-Options", "value": "nosniff" },
  { "key": "Cache-Control", "value": "no-cache" } ] } ] }
JSON
cd "$DIST" && vercel deploy --prod --yes --name multitravel-kayit
