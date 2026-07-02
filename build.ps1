param(
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
Write-Host "Building OpenGisDAF ($Configuration)..." -ForegroundColor Cyan
dotnet build OpenGisDAF.slnx -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Build succeeded." -ForegroundColor Green
