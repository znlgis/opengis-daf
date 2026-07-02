#!/bin/bash
set -e
CONFIGURATION=${1:-Release}
echo "Building OpenGisDAF ($CONFIGURATION)..."
dotnet build OpenGisDAF.slnx -c "$CONFIGURATION"
echo "Build succeeded."
