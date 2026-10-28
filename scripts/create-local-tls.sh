#!/usr/bin/env bash
# Creates a local CA and a LAN certificate. Keys never leave this directory.
set -euo pipefail
umask 077
tls_dir=${1:?Usage: create-local-tls.sh PRIVATE_DIRECTORY LAN_IP}
lan_ip=${2:?LAN IP is required}
[[ "$lan_ip" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || { echo 'Invalid IPv4 address' >&2; exit 1; }
mkdir -p "$tls_dir"
if [[ -e "$tls_dir/key.pem" || -e "$tls_dir/ca-key.pem" ]]; then
  echo 'Existing keys found; no files replaced.' >&2; exit 1
fi
openssl req -x509 -newkey rsa:3072 -nodes -days 3650 \
  -keyout "$tls_dir/ca-key.pem" -out "$tls_dir/ca.crt" -subj '/CN=Junkyard Private Local CA' \
  -addext 'basicConstraints=critical,CA:TRUE' -addext 'keyUsage=critical,keyCertSign,cRLSign' >/dev/null 2>&1
openssl req -new -newkey rsa:3072 -nodes -keyout "$tls_dir/key.pem" -out "$tls_dir/request.csr" -subj '/CN=Junkyard LAN' >/dev/null 2>&1
printf 'subjectAltName=IP:%s,IP:127.0.0.1,DNS:localhost\nbasicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\n' "$lan_ip" > "$tls_dir/extensions.cnf"
openssl x509 -req -in "$tls_dir/request.csr" -CA "$tls_dir/ca.crt" -CAkey "$tls_dir/ca-key.pem" -CAcreateserial \
  -out "$tls_dir/cert.pem" -days 365 -sha256 -extfile "$tls_dir/extensions.cnf" >/dev/null 2>&1
openssl verify -CAfile "$tls_dir/ca.crt" "$tls_dir/cert.pem"
echo 'Import only ca.crt on the operator device. Never share private keys.'
