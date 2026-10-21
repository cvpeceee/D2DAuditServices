#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

export PATH="${HOME}/.dotnet:${PATH}"
export DOTNET_ROOT="${HOME}/.dotnet"

if command -v docker >/dev/null 2>&1 && command -v dotnet >/dev/null 2>&1; then
  echo "Starting the Aspire AppHost (API, Postgres, object storage, web, Temporal)."
  echo "The Aspire dashboard shows logs and traces. Alternative: docker compose up --build"
  exec dotnet run --project "$root/src/aspire/D2DAudit.AppHost"
fi

echo "Docker is not available. Starting the API and Vite against Postgres on this machine."
echo "Alternative when Docker is installed: docker compose up --build"

if command -v pg_isready >/dev/null 2>&1; then
  pg_isready -h localhost -p 5432 -U d2d -d d2d_control >/dev/null 2>&1 || {
    echo "Postgres is not accepting connections for database d2d_control on localhost:5432."
    exit 1
  }
fi

export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__ControlPlane="${ConnectionStrings__ControlPlane:-Host=localhost;Port=5432;Database=d2d_control;Username=d2d;Password=d2d_dev_password}"

dotnet run --project src/api/src/D2DAudit.Api --urls http://localhost:8080 &
api_pid=$!
cleanup() { kill "$api_pid" >/dev/null 2>&1 || true; }
trap cleanup EXIT

for _ in $(seq 1 60); do
  if curl -fsS http://localhost:8080/health >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

cd src/web
exec npm run dev -- --host 0.0.0.0 --port 5173
