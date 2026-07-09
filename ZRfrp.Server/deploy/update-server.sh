#!/usr/bin/env bash
set -euo pipefail

case "$(uname -m)" in
  x86_64|amd64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) exit 1 ;;
esac

TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT
URL="https://github.com/3317603015whw-art/ZRfrp/releases/latest/download/zrfrp-server-${RID}.tar.gz"
curl --fail --location --retry 3 "${URL}" -o "${TMP}/server.tar.gz"
tar -xzf "${TMP}/server.tar.gz" -C "${TMP}"
cp -a "${TMP}/." /opt/zrfrp/server/
chown -R zrfrp:zrfrp /opt/zrfrp/server
systemd-run --unit=zrfrp-update-restart --on-active=2s /usr/bin/systemctl restart zrfrp-server zrfrp-frps
