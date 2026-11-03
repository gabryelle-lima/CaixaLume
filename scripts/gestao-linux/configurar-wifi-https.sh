#!/usr/bin/env bash
# Atualiza somente certificado de servidor e endpoint. Preserva CA, banco e credenciais.
set -euo pipefail
umask 077
[[ $EUID -eq 0 ]] || { echo 'Execute com sudo.' >&2; exit 1; }
old_ip=${1:-192.168.0.7}
wifi_ip=${2:-192.168.0.8}
python3 -c 'import ipaddress,sys; [ipaddress.IPv4Address(a) for a in sys.argv[1:]]' "$old_ip" "$wifi_ip"
ip -j -4 address show dev wlp2s0 | python3 -c 'import json,sys; addresses=json.load(sys.stdin); assert any(a.get("local")==sys.argv[1] for i in addresses for a in i.get("addr_info",[])),"IP nao encontrado no Wi-Fi; nenhuma alteracao feita"' "$wifi_ip"
exec 9>/run/caixalume-tls.lock
flock -n 9 || { echo 'Outra configuracao de TLS esta em andamento.' >&2; exit 1; }
pki=/etc/caixalume-gestao/pki
tls=/var/lib/caixalume-gestao/tls
conf=/etc/systemd/system/caixalume-gestao.service.d/https.conf
for file in "$pki/ca.crt" "$pki/ca.key" "$pki/server.key" "$tls/server.key" "$tls/server.crt" "$conf"; do
    [[ -f "$file" && ! -L "$file" ]] || { echo "Arquivo ausente ou link inesperado: $file" >&2; exit 1; }
done
# A chave de servidor atual e reutilizada; nao trocar a chave privada silenciosamente.
source_key=$(openssl pkey -in "$pki/server.key" -pubout 2>/dev/null | openssl dgst -sha256)
active_key=$(openssl pkey -in "$tls/server.key" -pubout 2>/dev/null | openssl dgst -sha256)
[[ "$source_key" == "$active_key" ]] || { echo 'Chaves de servidor diferentes; nenhuma alteracao feita.' >&2; exit 1; }
stamp="$(date -u +%Y%m%dT%H%M%SZ)-$$"
work="$pki/wifi-$stamp"
install -d -m 0700 "$work"
openssl req -new -key "$pki/server.key" -out "$work/server.csr" -subj '/CN=aurora' \
    -addext "subjectAltName=IP:$old_ip,IP:$wifi_ip,IP:127.0.0.1,DNS:aurora,DNS:localhost" \
    -addext 'basicConstraints=critical,CA:FALSE' \
    -addext 'keyUsage=critical,digitalSignature,keyEncipherment' \
    -addext 'extendedKeyUsage=serverAuth'
openssl x509 -req -in "$work/server.csr" -CA "$pki/ca.crt" -CAkey "$pki/ca.key" -CAcreateserial \
    -out "$work/server.crt" -days 365 -sha256 -copy_extensions copy
for address in "$old_ip" "$wifi_ip"; do
    openssl verify -CAfile "$pki/ca.crt" -purpose sslserver -verify_ip "$address" "$work/server.crt"
done
backup="/var/backups/caixalume-gestao/tls-$stamp"
install -d -m 0700 "$backup"
cp -- "$tls/server.crt" "$backup/server.crt"
cp -- "$conf" "$backup/https.conf"
changed=0
rollback() {
    error=$?
    trap - ERR
    if [[ $changed -eq 1 ]]; then
        echo 'Falha na verificacao; restaurando certificado e configuracao anteriores.' >&2
        install -m 0640 -o root -g caixalume-gestao "$backup/server.crt" "$tls/server.crt"
        install -m 0644 "$backup/https.conf" "$conf"
        systemctl daemon-reload
        systemctl restart caixalume-gestao.service || true
    fi
    exit "$error"
}
trap rollback ERR
changed=1
install -m 0640 -o root -g caixalume-gestao "$work/server.crt" "$tls/server.crt"
printf '%s\n' '[Service]' \
    'Environment="ASPNETCORE_URLS=http://127.0.0.1:5187;https://0.0.0.0:5443"' \
    'Environment="ASPNETCORE_Kestrel__Certificates__Default__Path=/var/lib/caixalume-gestao/tls/server.crt"' \
    'Environment="ASPNETCORE_Kestrel__Certificates__Default__KeyPath=/var/lib/caixalume-gestao/tls/server.key"' > "$work/https.conf"
install -m 0644 "$work/https.conf" "$conf"
systemctl daemon-reload
systemctl restart caixalume-gestao.service
healthy=0
for attempt in {1..20}; do
    if curl --fail --silent --connect-timeout 2 --max-time 4 --cacert "$pki/ca.crt" "https://$wifi_ip:5443/health" >/dev/null; then healthy=1; break; fi
    sleep 1
done
[[ $healthy -eq 1 ]]
curl --fail --silent --max-time 5 http://127.0.0.1:5187/health >/dev/null
trap - ERR
echo "SUCESSO: HTTPS validado em https://$wifi_ip:5443 e painel local preservado."
echo "Backup da configuracao anterior: $backup"
echo 'Banco, vinculos e chave administrativa preservados. Nenhuma porta foi aberta no roteador.'
