Set-Location (Join-Path $PSScriptRoot '..')
dotnet restore .\src\Finpulse.Web\Finpulse.Web.csproj
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project .\src\Finpulse.Web\Finpulse.Web.csproj -- --init-local
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project .\src\Finpulse.Web\Finpulse.Web.csproj
