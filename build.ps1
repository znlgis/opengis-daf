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

Write-Host "Running integration tests..." -ForegroundColor Cyan
dotnet test tests/OpenGisDAF.IntegrationTests/OpenGisDAF.IntegrationTests.csproj -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Tests passed." -ForegroundColor Green
