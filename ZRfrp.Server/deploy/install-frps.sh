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
URL="${ZRFRP_FRP_URL:-https://github.com/fatedier/frp/releases/download/v${FRP_VERSION}/frp_${FRP_VERSION}_linux_${FRP_ARCH}.tar.gz}"
if ! curl --fail --location --retry 8 --retry-all-errors --retry-delay 2 \
  --connect-timeout 20 --speed-time 30 --speed-limit 1024 \
  "${URL}" -o "${TMP}/frp.tar.gz"; then
  cat >&2 <<EOF
frps 下载失败，通常是当前服务器访问 GitHub Release CDN 超时或返回 5xx。
请稍后重试，或通过 ZRFRP_FRP_URL 指定可访问的 frp 压缩包地址。
EOF
  exit 22
fi
tar -xzf "${TMP}/frp.tar.gz" -C "${TMP}"
install -d -o zrfrp -g zrfrp /opt/zrfrp /etc/zrfrp /var/log/zrfrp
install -m 0755 "${TMP}/frp_${FRP_VERSION}_linux_${FRP_ARCH}/frps" /opt/zrfrp/frps.new
mv -f /opt/zrfrp/frps.new /opt/zrfrp/frps
if [[ ! -f /etc/zrfrp/frps.toml ]]; then
  TOKEN="$(openssl rand -hex 24)"
  PASSWORD="$(openssl rand -hex 18)"
  sed -e "0,/CHANGE_ME/s//${TOKEN}/" -e "0,/CHANGE_ME/s//${PASSWORD}/" \
    /opt/zrfrp/server/deploy/frps.toml.example >/etc/zrfrp/frps.toml
fi
chown root:zrfrp /etc/zrfrp /etc/zrfrp/frps.toml
chmod 0770 /etc/zrfrp
chmod 0640 /etc/zrfrp/frps.toml
systemctl daemon-reload
systemctl enable --now zrfrp-frps
