#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DIST="$ROOT/dist"
DEPS="$DIST/gdeps"
MODS="$DIST/gmods"
PROJECT="$ROOT/src/GLoader/GLoader.csproj"
LAUNCHER="$ROOT/src/native/gloader.c"
PROFILER="$ROOT/src/native/profiler.c"
FACADES="$ROOT/third_party/mono-facades"

rm -rf "$DIST"
mkdir -p "$DEPS" "$MODS"

dotnet build "$PROJECT" \
  -c Release \
  -o "$DEPS" \
  --nologo

test -f "$DEPS/GLoader.dll"
test -f "$FACADES/SHA256SUMS"
test -f "$FACADES/System.Runtime.dll"

(
  cd "$FACADES"
  sha256sum -c SHA256SUMS
)

cp "$FACADES"/*.dll "$DEPS/"
test -f "$DEPS/System.Runtime.dll"

CC_VALUE="$(printenv CC || true)"
if [ -z "$CC_VALUE" ]; then
  CC_VALUE=cc
fi

COMMON_CFLAGS=(
  -std=c11
  -O2
  -pipe
  -Wall
  -Wextra
  -Wl,-z,relro
  -Wl,-z,now
  -Wl,-z,noexecstack
)

"$CC_VALUE" \
  "${COMMON_CFLAGS[@]}" \
  -fPIE \
  -pie \
  "$LAUNCHER" \
  -o "$DIST/gloader"

"$CC_VALUE" \
  "${COMMON_CFLAGS[@]}" \
  -fPIC \
  -shared \
  "$PROFILER" \
  -ldl \
  -o "$DEPS/libmono-profiler-gloader.so"

chmod 0755 "$DIST/gloader"
chmod 0755 "$DEPS/libmono-profiler-gloader.so"

cat > "$MODS/README.txt" <<'MODREADME'
Place enabled raw C# source-mod folders directly in this directory.
Rename a mod directory to *.disabled to disable it.

Historical gmods in the source repository are porting inputs and are not
automatically bundled as enabled Linux defaults until individually verified.
MODREADME

cp "$ROOT/LICENSE.md" "$DEPS/LICENSE.md"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$DEPS/THIRD-PARTY-NOTICES.txt"

printf '\nBuilt Linux gloader package: %s\n' "$DIST"
printf 'Install its contents directly into the existing Terraria directory.\n'
printf 'Steam launch option: ./gloader %%command%%\n'
