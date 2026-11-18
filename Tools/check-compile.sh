#!/usr/bin/env bash
# Compile-checks all runtime C# under Assets/ against Unity 2021.3 reference assemblies.
# Needs: dotnet SDK 8, curl, unzip. Editor scripts (Assets/**/Editor) are NOT checked.
set -euo pipefail
DIR="$(cd "$(dirname "$0")/CompileCheck" && pwd)"
REF="$DIR/.unityref"
if [ ! -d "$REF/lib" ]; then
  mkdir -p "$REF/lib"
  curl -sSL -o "$REF/m.nupkg" https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
  unzip -qo "$REF/m.nupkg" -d "$REF/x"
  find "$REF/x" -name '*.dll' -exec cp {} "$REF/lib/" \;
fi
cd "$DIR"
dotnet build -nologo -v q -clp:NoSummary "$@" CompileCheck.csproj
