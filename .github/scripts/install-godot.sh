#!/usr/bin/env bash
# Descarga Godot .NET (Linux) para CI y exporta la variable GODOT con la ruta del ejecutable.
set -euo pipefail

name="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64"
curl -fsSL -o godot.zip "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/${name}.zip"
unzip -q godot.zip -d "$HOME/godot"
bin=$(find "$HOME/godot" -maxdepth 2 -type f -name 'Godot_v*mono_linux*x86_64' | head -1)
chmod +x "$bin"
echo "GODOT=$bin" >> "$GITHUB_ENV"
"$bin" --version
