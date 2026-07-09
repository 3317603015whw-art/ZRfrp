#!/usr/bin/env bash
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "请使用 root 运行此修复脚本。" >&2
  exit 1
fi

if findmnt -no OPTIONS -T /opt/zrfrp 2>/dev/null | tr ',' '\n' | grep -qx ro; then
  mount -o remount,rw / || true
fi

if findmnt -no OPTIONS -T /opt/zrfrp 2>/dev/null | tr ',' '\n' | grep -qx ro; then
  echo "/opt/zrfrp 所在文件系统仍为只读，请先检查云服务器磁盘或重启服务器。" >&2
  exit 30
fi

id -u zrfrp >/dev/null 2>&1 || useradd --system --home /var/lib/zrfrp --shell /usr/sbin/nologin zrfrp
install -d -o zrfrp -g zrfrp /opt/zrfrp /etc/zrfrp /var/lib/zrfrp /var/log/zrfrp
chattr -i /opt/zrfrp/frps 2>/dev/null || true
chown -R zrfrp:zrfrp /opt/zrfrp /var/lib/zrfrp /var/log/zrfrp
chmod -R u+rwX /opt/zrfrp /var/lib/zrfrp /var/log/zrfrp
if [[ -f /etc/zrfrp/frps.toml ]]; then
  chown root:zrfrp /etc/zrfrp /etc/zrfrp/frps.toml
  chmod 0770 /etc/zrfrp
  chmod 0640 /etc/zrfrp/frps.toml
fi

/usr/local/sbin/zrfrp-install-frps
systemctl daemon-reload
systemctl restart zrfrp-frps
systemctl is-active --quiet zrfrp-frps
echo "frps 修复完成并已启动。"
