#!/usr/bin/env bash
set -euo pipefail

FRP_VERSION="${FRP_VERSION:-0.69.1}"
case "$(uname -m)" in
  x86_64|amd64) FRP_ARCH="amd64" ;;
  aarch64|arm64) FRP_ARCH="arm64" ;;
  *) echo "不支持的架构" >&2; exit 1 ;;
esac

TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT
URL="https://github.com/fatedier/frp/releases/download/v${FRP_VERSION}/frp_${FRP_VERSION}_linux_${FRP_ARCH}.tar.gz"
curl --fail --location --retry 3 "${URL}" -o "${TMP}/frp.tar.gz"
tar -xzf "${TMP}/frp.tar.gz" -C "${TMP}"
install -m 0755 "${TMP}/frp_${FRP_VERSION}_linux_${FRP_ARCH}/frps" /opt/zrfrp/frps
install -d -o zrfrp -g zrfrp /etc/zrfrp /var/log/zrfrp
if [[ ! -f /etc/zrfrp/frps.toml ]]; then
  TOKEN="$(openssl rand -hex 24)"
  PASSWORD="$(openssl rand -hex 18)"
  sed -e "0,/CHANGE_ME/s//${TOKEN}/" -e "0,/CHANGE_ME/s//${PASSWORD}/" \
    /opt/zrfrp/server/deploy/frps.toml.example >/etc/zrfrp/frps.toml
  chown zrfrp:zrfrp /etc/zrfrp/frps.toml
  chmod 0660 /etc/zrfrp/frps.toml
fi
systemctl daemon-reload
systemctl enable --now zrfrp-frps
