#!/usr/bin/env bash
# Compile GrappleHook and its test projects without deploying, then run the tests.
#   --build-only  compile everything, run nothing
# Building the mod project WITHOUT this script deploys it to the game; see README.md.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
build_only=false
for arg in "$@"; do
  case "$arg" in
    --build-only) build_only=true ;;
    --help) echo 'Usage: bash tools/validate.sh [--build-only]'; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done
dotnet build tests/GrappleHook.slnx -m:1 -nr:false -p:MdkBuildConfiguration=NoPackage -v quiet
if "$build_only"; then exit 0; fi
dotnet test tests/GrappleHook.Tests --no-build --no-restore -v quiet \
  --results-directory out/validation --logger 'trx;LogFileName=validation.trx'
