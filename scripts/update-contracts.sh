#!/usr/bin/env bash
# Rebuilds lib/arcfrag/{ArcfragCore.Contract,Arcfrag.Contracts}.dll from an arcfrag-core checkout (compile-time only).
# ArcfragCore ships both as exports (addons/swiftlys2/plugins/ArcfragCore/resources/exports/), SwiftlyS2 loads them once
# for every plugin, so CSRoll never ships its own copy. They are committed here because arcfrag-core is private and
# CI cannot fetch it. Run after a contract change in arcfrag-core and commit the result.
# Usage: scripts/update-contracts.sh <path-to-arcfrag-core>   (the checkout at the core version the plugin targets)
set -euo pipefail
core="$(cd "${1:?usage: scripts/update-contracts.sh <path-to-arcfrag-core>}" && pwd)"
cd "$(dirname "$0")/.."
out="$(mktemp -d)"; trap 'rm -rf "$out"' EXIT
dotnet build "$core/src/ArcfragCore.Contract/ArcfragCore.Contract.csproj" -c Release -o "$out" >/dev/null
mkdir -p lib/arcfrag
cp "$out/ArcfragCore.Contract.dll" "$out/ArcfragCore.Contract.xml" "$out/Arcfrag.Contracts.dll" lib/arcfrag/
sed -n 's#.*<Version>\(.*\)</Version>.*#ArcfragCore.Contract \1#p' "$core/src/ArcfragCore.Contract/ArcfragCore.Contract.csproj" > lib/arcfrag/VERSION
echo "from $(git -C "$core" rev-parse --short HEAD 2>/dev/null || echo unknown) ($(git -C "$core" rev-parse --abbrev-ref HEAD 2>/dev/null || echo ?))" >> lib/arcfrag/VERSION
cat lib/arcfrag/VERSION
