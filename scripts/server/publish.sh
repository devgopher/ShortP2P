#!/usr/bin/env bash
# Publishes ShortP2P.MessengerServer.Api as a self-contained folder.
# Usage (repo root or scripts/server):
#   ./scripts/server/publish.sh
#   ./scripts/server/publish.sh win-x64
#   ./scripts/server/publish.sh linux-x64
set -euo pipefail

RID="${1:-all}"
CONFIGURATION="${2:-Release}"

find_repo_root() {
  local dir="$1"
  while [ -n "$dir" ] && [ "$dir" != "/" ]; do
    if [ -f "$dir/src/Server/ShortP2P.MessengerServer.Api/ShortP2P.MessengerServer.Api.csproj" ]; then
      printf '%s\n' "$dir"
      return 0
    fi
    dir="$(dirname "$dir")"
  done
  return 1
}

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(find_repo_root "$script_dir" || true)"
if [ -z "${repo_root}" ]; then
  repo_root="$(find_repo_root "$(pwd)" || true)"
fi
if [ -z "${repo_root}" ]; then
  echo "Cannot find the ShortP2P repo root (looked for ShortP2P.MessengerServer.Api.csproj)." >&2
  exit 1
fi

csproj="$repo_root/src/Server/ShortP2P.MessengerServer.Api/ShortP2P.MessengerServer.Api.csproj"

read_version() {
  local informational
  local version
  informational="$(sed -n 's/.*<InformationalVersion>\s*\([^<][^<]*\)\s*<\/InformationalVersion>.*/\1/p' "$csproj" | head -n 1 | tr -d '[:space:]')"
  if [ -n "$informational" ]; then
    printf '%s\n' "$informational"
    return 0
  fi
  version="$(sed -n 's/.*<Version>\s*\([0-9][^<]*\)\s*<\/Version>.*/\1/p' "$csproj" | head -n 1 | tr -d '[:space:]')"
  if [ -n "$version" ]; then
    printf '%s\n' "$version"
    return 0
  fi
  if [ -f "$script_dir/VERSION" ]; then
    tr -d '[:space:]' < "$script_dir/VERSION"
    return 0
  fi
  printf '0.1.0\n'
}

version="$(read_version)"
out_root="$script_dir/out"

case "$RID" in
  all) rids="win-x64 linux-x64" ;;
  win-x64|linux-x64) rids="$RID" ;;
  *)
    echo "Unknown RID: $RID (expected win-x64, linux-x64, or all)" >&2
    exit 1
    ;;
esac

echo "Repo:    $repo_root"
echo "Version: $version"
echo "RIDs:    $rids"

for runtime in $rids; do
  dest="$out_root/$runtime"
  rm -rf "$dest"
  mkdir -p "$dest"
  echo "Publishing $runtime -> $dest"
  dotnet publish "$csproj" \
    -c "$CONFIGURATION" \
    -r "$runtime" \
    --self-contained true \
    -o "$dest"
done

echo "Publish finished. Output: $out_root"
