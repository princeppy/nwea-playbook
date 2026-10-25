#!/usr/bin/env bash
# Download NWEA MAP reports (ASG + Class Profile) for one term, with the C# runner.
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

cd "$(dirname "$0")"          # dotnet/: build and tests run here
ROOT="$(cd .. && pwd)"
PLAYBOOK="nwea-class-profile.yaml"
PROJECT="src/PlaybookRunner/PlaybookRunner.csproj"
OUT="$PWD/src/PlaybookRunner/bin/Release/net10.0"

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET is not installed. Install the .NET 10 SDK from https://dotnet.microsoft.com/download" >&2
  exit 1
fi
DOTNET_MAJOR="$(dotnet --version | cut -d. -f1)"
if [ "$DOTNET_MAJOR" -lt 10 ]; then
  echo ".NET SDK 10 or newer is needed (found $(dotnet --version))." >&2
  exit 1
fi

if [ "${1:-}" = "test" ]; then
  exec dotnet test --solution PlaybookLab.slnx
fi

FIRST_RUN=false
[ -f "$OUT/PlaybookRunner.dll" ] || FIRST_RUN=true
dotnet build "$PROJECT" -c Release -nologo -v quiet -clp:NoSummary >/dev/null || {
  echo "Build failed. Run: dotnet build $PROJECT" >&2
  exit 1
}
if [ "$FIRST_RUN" = true ]; then
  echo "First run: installing Playwright's Chromium..."
  dotnet "$OUT/PlaybookRunner.dll" --install-browsers
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

exec dotnet "$OUT/PlaybookRunner.dll" "$PLAYBOOK" "${args[@]+"${args[@]}"}" "$@"
