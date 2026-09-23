#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DIST="$ROOT/dist"
DEPS="$DIST/gdeps"
MODS="$DIST/gmods"
PROJECT="$ROOT/src/GLoader/GLoader.csproj"
NATIVE="$ROOT/src/native/gloader.c"

rm -rf "$DIST"
mkdir -p "$DEPS" "$MODS"

dotnet build "$PROJECT" \
  -c Release \
  -o "$DEPS" \
  --nologo

test -f "$DEPS/GLoader.dll"

CC_VALUE="$(printenv CC || true)"
if [ -z "$CC_VALUE" ]; then
  CC_VALUE=cc
fi

"$CC_VALUE" \
  -std=c11 \
  -O2 \
  -pipe \
  -fPIE \
  -pie \
  -Wall \
  -Wextra \
  -Wl,-z,relro \
  -Wl,-z,now \
  -Wl,-z,noexecstack \
  -Wl,--disable-new-dtags \
  -Wl,-rpath,'$ORIGIN/lib64:$ORIGIN/lib:$ORIGIN' \
  "$NATIVE" \
  -ldl \
  -o "$DIST/gloader"

chmod 0755 "$DIST/gloader"

cat > "$MODS/README.txt" <<'EOF'
Place enabled raw C# source-mod folders directly in this directory.
Rename a mod directory to *.disabled to disable it.

Historical gmods in the source repository are porting inputs and are not
automatically bundled as enabled Linux defaults until individually verified.
EOF

cp "$ROOT/LICENSE.md" "$DEPS/LICENSE.md"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$DEPS/THIRD-PARTY-NOTICES.txt"

printf '\nBuilt Linux gloader package: %s\n' "$DIST"
printf 'Install its contents directly into the existing Terraria directory.\n'
printf 'Steam launch option: ./gloader %%command%%\n'
