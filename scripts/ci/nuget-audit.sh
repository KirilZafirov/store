#!/usr/bin/env bash
set -euo pipefail

output="${1:-nuget-audit.txt}"

dotnet list RetailPlatform.slnx package --vulnerable --include-transitive > "$output"
cat "$output"

if grep -Eiq '(^[[:space:]]*>[[:space:]]|[[:space:]](critical|high|moderate|low)[[:space:]])' "$output"; then
  echo "NuGet vulnerability audit found vulnerable packages." >&2
  exit 1
fi
