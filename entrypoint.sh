#!/bin/sh
set -e

echo "[entrypoint] starting..."

# If no PFX exists, generate a self-signed cert at container startup (dev only, untrusted)
if [ ! -f /app/localhost.pfx ]; then
  echo "[entrypoint] generating self-signed certificate at /app/localhost.pfx"
  openssl req -x509 -newkey rsa:4096 -keyout /tmp/localhost.key -out /tmp/localhost.crt \
    -days 365 -nodes -subj "/CN=localhost"
  openssl pkcs12 -export -out /app/localhost.pfx -inkey /tmp/localhost.key -in /tmp/localhost.crt -passout pass:
  rm -f /tmp/localhost.key /tmp/localhost.crt
else
  echo "[entrypoint] found existing /app/localhost.pfx, skipping generation"
fi

# Ensure the certificate path env is set (unless user provided custom path)
if [ -z "$ASPNETCORE_Kestrel__Certificates__Default__Path" ]; then
  export ASPNETCORE_Kestrel__Certificates__Default__Path=/app/localhost.pfx
fi

echo "[entrypoint] launching application"
exec dotnet SBB.EasyRide.TaxReport.Web.dll
