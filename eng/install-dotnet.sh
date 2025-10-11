#!/usr/bin/env bash
set -euo pipefail

CHANNEL="9.0"
INSTALL_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESOLVED_INSTALL_DIR="${INSTALL_DIR}"
TEMP_SCRIPT="${SCRIPT_DIR}/dotnet-install.sh"

cleanup() {
  rm -f "${TEMP_SCRIPT}"
}
trap cleanup EXIT

if ! command -v wget >/dev/null 2>&1 && ! command -v curl >/dev/null 2>&1; then
  echo "Either wget or curl is required to download dotnet-install.sh" >&2
  exit 1
fi

DOWNLOAD_TOOL="wget"
DOWNLOAD_ARGS=("-q" "-O")
URL="https://dot.net/v1/dotnet-install.sh"

if command -v curl >/dev/null 2>&1; then
  DOWNLOAD_TOOL="curl"
  DOWNLOAD_ARGS=("-sSL" "-o")
fi

${DOWNLOAD_TOOL} "${DOWNLOAD_ARGS[@]}" "${TEMP_SCRIPT}" "${URL}"
chmod +x "${TEMP_SCRIPT}"

"${TEMP_SCRIPT}" --channel "${CHANNEL}" --install-dir "${INSTALL_DIR}" --no-path

cat <<EON
Add the following to your shell profile to use the installed .NET SDK:

    export DOTNET_ROOT="${RESOLVED_INSTALL_DIR}"
    export PATH="\$DOTNET_ROOT:\$PATH"

EON
