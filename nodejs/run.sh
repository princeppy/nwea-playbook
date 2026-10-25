#!/usr/bin/env bash
# Download NWEA MAP reports (ASG + Class Profile) for one term.
#
# The playbook, .env, downloads/ and runs/ are shared and live in the repo root (one level up).
#
#   ./run.sh                          term from NWEA_TERM in .env
#   ./run.sh "Spring 2025-2026"       this term instead
#   ./run.sh "Fall 2026-2027" --headed   watch the browser (also: --keep, --slow=300)
#   ./run.sh test                     run the test suite
#
# Exit codes: 0 all good, 1 stopped by an error, 2 finished but some items failed.
set -euo pipefail

cd "$(dirname "$0")"          # nodejs/: npm install and tests run here
ROOT="$(cd .. && pwd)"
PLAYBOOK="nwea-class-profile.yaml"

if ! command -v node >/dev/null 2>&1; then
  echo "Node.js is not installed. Install Node 22 or newer from https://nodejs.org" >&2
  exit 1
fi
NODE_MAJOR="$(node -p 'process.versions.node.split(".")[0]')"
if [ "$NODE_MAJOR" -lt 22 ]; then
  echo "Node.js 22 or newer is needed (found $(node -v))." >&2
  exit 1
fi

if [ ! -d node_modules ]; then
  echo "First run: installing dependencies and the Chromium browser..."
  npm ci
  npx playwright install chromium
fi

if [ "${1:-}" = "test" ]; then
  exec npm test
fi

cd "$ROOT"                    # the run itself works from the repo root
if [ ! -f .env ]; then
  echo "No .env file in $ROOT. Create one with NWEA_USER, NWEA_PASS and NWEA_TERM (see README.md)." >&2
  exit 1
fi

args=()
# A first argument that is not a flag or name=value is the term, e.g. "Fall 2026-2027".
if [ $# -gt 0 ] && [[ "$1" != -* ]] && [[ "$1" != *=* ]]; then
  args+=("term=$1")
  shift
fi

exec nodejs/node_modules/.bin/tsx nodejs/run.ts "$PLAYBOOK" "${args[@]+"${args[@]}"}" "$@"
