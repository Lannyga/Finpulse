$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    & dotnet build FINPULSE.slnx -c Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion.' }
    & dotnet tests/Finpulse.WebChecks/bin/Release/net10.0/Finpulse.WebChecks.dll --oracle | Tee-Object -FilePath docs/qa/web-oracle-checks.log
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas Oracle no se completaron. No se declara terminada la integracion.' }
} finally { Pop-Location }
