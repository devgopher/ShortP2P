#!/usr/bin/env bash
# Stages a Debian package tree and, if dpkg-deb is available, builds the .deb.
# Usage (repo root, scripts/server, or scripts/server/debian):
#   ./scripts/server/debian/build-deb.sh
#   ./scripts/server/debian/build-deb.sh --skip-publish
set -euo pipefail

SKIP_PUBLISH=0
CONFIGURATION=Release
for arg in "$@"; do
  case "$arg" in
    --skip-publish) SKIP_PUBLISH=1 ;;
    --configuration=*) CONFIGURATION="${arg#*=}" ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 1
      ;;
  esac
done

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
server_scripts="$(cd "$script_dir/.." && pwd)"
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
  if [ -f "$server_scripts/VERSION" ]; then
    tr -d '[:space:]' < "$server_scripts/VERSION"
    return 0
  fi
  printf '0.1.0\n'
}

version="$(read_version)"
pkg_version="${version}-1"
publish_dir="$server_scripts/out/linux-x64"
staging="$server_scripts/out/deb-staging"
deb_out="$server_scripts/out/deb"

if [ "$SKIP_PUBLISH" -eq 0 ]; then
  "$server_scripts/publish.sh" linux-x64 "$CONFIGURATION"
fi

exe="$publish_dir/ShortP2P.MessengerServer.Api"
if [ ! -f "$exe" ] && [ ! -f "$exe.dll" ]; then
  echo "Publish output not found: $publish_dir" >&2
  echo "Run: $server_scripts/publish.sh linux-x64" >&2
  exit 1
fi

rm -rf "$staging"
mkdir -p \
  "$staging/DEBIAN" \
  "$staging/opt/shortp2p-messengerserver" \
  "$staging/etc/shortp2p/certs" \
  "$staging/var/lib/shortp2p/data" \
  "$staging/usr/lib/systemd/system" \
  "$staging/usr/share/shortp2p-messengerserver" \
  "$staging/usr/share/doc/shortp2p-messengerserver"

# Binaries (no user data, no PFX).
tar -C "$publish_dir" \
  --exclude='*.litedb' \
  --exclude='*.pfx' \
  --exclude='appsettings.Production.json' \
  --exclude='appsettings.Development.json' \
  -cf - . | tar -C "$staging/opt/shortp2p-messengerserver" -xf -

if [ -f "$staging/opt/shortp2p-messengerserver/ShortP2P.MessengerServer.Api" ]; then
  chmod 755 "$staging/opt/shortp2p-messengerserver/ShortP2P.MessengerServer.Api"
fi

install -m 644 "$script_dir/appsettings.Production.json.example" \
  "$staging/usr/share/shortp2p-messengerserver/appsettings.Production.json.example"
install -m 644 "$script_dir/appsettings.Production.json.example" \
  "$staging/etc/shortp2p/appsettings.Production.json"
ln -sfn /etc/shortp2p/appsettings.Production.json \
  "$staging/opt/shortp2p-messengerserver/appsettings.Production.json"

install -m 644 "$script_dir/shortp2p-messengerserver.service" \
  "$staging/usr/lib/systemd/system/shortp2p-messengerserver.service"

# Docs
sed "s/0\\.1\\.0-1/${pkg_version}/g" "$script_dir/changelog" \
  > "$staging/usr/share/doc/shortp2p-messengerserver/changelog"
install -m 644 "$script_dir/conffiles" "$staging/DEBIAN/conffiles"
install -m 755 "$script_dir/postinst" "$staging/DEBIAN/postinst"
install -m 755 "$script_dir/prerm" "$staging/DEBIAN/prerm"
install -m 755 "$script_dir/postrm" "$staging/DEBIAN/postrm"

size_kb="$(du -sk "$staging" | awk '{print $1}')"
{
  echo "Package: shortp2p-messengerserver"
  echo "Version: $pkg_version"
  echo "Section: net"
  echo "Priority: optional"
  echo "Architecture: amd64"
  echo "Installed-Size: $size_kb"
  echo "Depends: libc6, libgcc-s1, libstdc++6, zlib1g"
  echo "Recommends: postgresql"
  echo "Maintainer: ShortP2P packagers <noreply@shortp2p.local>"
  echo "Description: ShortP2P messenger server (standalone Kestrel node)"
  echo " Dedicated HTTPS messenger-server node. Postgres is optional"
  echo " and is not required to install or start the service."
} > "$staging/DEBIAN/control"

mkdir -p "$deb_out"
deb_name="shortp2p-messengerserver_${pkg_version}_amd64.deb"

echo "Staged package tree: $staging"
echo "Version: $pkg_version"

if command -v dpkg-deb >/dev/null 2>&1; then
  dpkg-deb --root-owner-group --build "$staging" "$deb_out/$deb_name"
  echo "Built $deb_out/$deb_name"
else
  echo "dpkg-deb is not installed. Staging is ready."
  echo "On a Debian/Ubuntu host run:"
  echo "  dpkg-deb --root-owner-group --build \"$staging\" \"$deb_out/$deb_name\""
fi
