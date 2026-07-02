#!/bin/bash
set -e
cd "$(dirname "$0")"
CONFIGURATION=${1:-Release}
echo "Building OpenGisDAF ($CONFIGURATION)..."
dotnet build OpenGisDAF.slnx -c "$CONFIGURATION"
echo "Build succeeded."
