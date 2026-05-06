#!/usr/bin/env bash
set -euo pipefail

dotnet tool restore || dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet restore
dotnet build

for attempt in {1..30}; do
  if dotnet ef database update --project TodoApi --startup-project TodoApi; then
    exit 0
  fi

  echo "SQL Server is not ready yet. Retrying migration attempt ${attempt}/30..."
  sleep 5
done

echo "SQL Server did not become ready in time for EF migrations." >&2
exit 1
