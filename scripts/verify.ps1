param([switch]$Container)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet restore tests/Reservations.Api.Tests/Reservations.Api.Tests.csproj --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
    dotnet test tests/Reservations.Api.Tests/Reservations.Api.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=integration.trx' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    dotnet publish src/Reservations.Api/Reservations.Api.csproj -c Release --no-restore -o artifacts/publish /p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Container) {
        docker build -t reservations-api:local .
        if ($LASTEXITCODE -ne 0) { throw 'Container build failed.' }
    }
}
finally {
    Pop-Location
}