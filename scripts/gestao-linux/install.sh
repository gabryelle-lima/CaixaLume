#!/usr/bin/env bash
# Instalação local assistida. Não expõe portas na rede/internet.
set -euo pipefail
umask 027
if [[ ${EUID} -ne 0 ]]; then echo "Execute com sudo bash install.sh" >&2; exit 1; fi
source /etc/os-release
if [[ "$ID" != ubuntu || "$(uname -m)" != x86_64 ]]; then echo "Pacote preparado para Ubuntu x86_64." >&2; exit 1; fi
bundle=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
cd "$bundle"
sha256sum --check SHA256SUMS
apt-get update
apt-get install -y aspnetcore-runtime-10.0 libsqlite3-0 sqlite3 openssl curl
getent passwd caixalume-gestao >/dev/null || useradd --system --user-group --home-dir /var/lib/caixalume-gestao --shell /usr/sbin/nologin caixalume-gestao
# Definir também o pai: com umask 027, pais implícitos podem ficar 0750 root:root.
install -d -m 0755 -o root -g root /opt/caixalume-gestao /opt/caixalume-gestao/releases
install -d -m 0750 -o caixalume-gestao -g caixalume-gestao /var/lib/caixalume-gestao
install -d -m 0700 /etc/caixalume-gestao /var/backups/caixalume-gestao
# Credencial administrativa persistente, criada apenas na primeira instalação.
if [[ ! -e /etc/caixalume-gestao/server.env ]]; then
    admin_token=$(openssl rand -hex 32)
    printf 'CAIXALUME_ADMIN_TOKEN=%s\nCAIXALUME_MANAGEMENT_DB=/var/lib/caixalume-gestao/management.db\n' "$admin_token" > /etc/caixalume-gestao/server.env
    unset admin_token
fi
chmod 0600 /etc/caixalume-gestao/server.env
release="$(date -u +%Y%m%dT%H%M%SZ)-$$"
target="/opt/caixalume-gestao/releases/$release"
install -d -m 0755 "$target"
cp -R "$bundle/app/." "$target/"
chown -R root:root "$target"
find "$target" -type d -exec chmod 0755 {} +
find "$target" -type f -exec chmod 0644 {} +
old_target=$(readlink /opt/caixalume-gestao/current || true)
systemctl stop caixalume-gestao.service 2>/dev/null || true
if [[ -f /var/lib/caixalume-gestao/management.db ]]; then
    sqlite3 /var/lib/caixalume-gestao/management.db ".backup '/var/backups/caixalume-gestao/before-$release.db'"
    chmod 0600 "/var/backups/caixalume-gestao/before-$release.db"
    if [[ -d /var/lib/caixalume-gestao/management.db.authkeys ]]; then
        cp -a /var/lib/caixalume-gestao/management.db.authkeys "/var/backups/caixalume-gestao/before-$release.authkeys"
    fi
fi
ln -s "$target" /opt/caixalume-gestao/current.next
mv -Tf /opt/caixalume-gestao/current.next /opt/caixalume-gestao/current
install -m 0644 "$bundle/caixalume-gestao.service" /etc/systemd/system/caixalume-gestao.service
systemctl daemon-reload
systemctl enable caixalume-gestao.service
systemctl start caixalume-gestao.service || true
for attempt in {1..30}; do
    if systemctl is-active --quiet caixalume-gestao.service && curl --fail --silent http://127.0.0.1:5187/health >/dev/null; then
        echo "Gestão instalada e respondendo em http://127.0.0.1:5187 neste servidor."
        echo "Não foram abertas portas externas. Use o túnel SSH para administrar."
        echo "Primeiro acesso: configure sua conta no painel. A chave atual so sera usada uma vez no cadastro inicial."
        exit 0
    fi
    sleep 1
done
echo "O serviço não respondeu. Consulte: sudo journalctl -u caixalume-gestao -n 40 --no-pager" >&2
# Reverte só os executáveis. Não restaura/apaga banco silenciosamente.
if [[ -n "$old_target" && -d "$old_target" ]]; then
    systemctl stop caixalume-gestao.service || true
    ln -s "$old_target" /opt/caixalume-gestao/rollback.next
    mv -Tf /opt/caixalume-gestao/rollback.next /opt/caixalume-gestao/current
    systemctl start caixalume-gestao.service || true
    echo "Executáveis anteriores restaurados; banco preservado." >&2
fi
exit 1
