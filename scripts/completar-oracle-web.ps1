# Continuacion autorizada del aprovisionamiento local. Credenciales solo por entrada oculta.
param([switch]$RestablecerClaveWeb)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$statusDirectory = Join-Path $root 'tests\.qa'
New-Item -ItemType Directory -Force -Path $statusDirectory | Out-Null
$statusPath = Join-Path $statusDirectory 'oracle-completion.json'
$stage = 'Preparando'
function Set-Stage([string]$Value) {
    $script:stage = $Value
    @{ Stage=$Value; Success=$false; UpdatedAt=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    Write-Host ('FINPULSE: ' + $Value)
}
try {
    $Host.UI.RawUI.WindowTitle = 'FINPULSE - conexion Oracle segura'
    Set-Stage 'Reanudar FINPULSE_WEB - entrada local oculta'
    & (Join-Path $PSScriptRoot 'configurar-oracle-web.ps1') -ReanudarEsquemas -Esquemas FINPULSE_WEB -AutorizarDDL -RestablecerClave:$RestablecerClaveWeb
    Set-Stage 'Crear FINPULSE_WEB_TEST - elige su contrasena local'
    & (Join-Path $PSScriptRoot 'configurar-oracle-web.ps1') -CrearEsquemas -Esquemas FINPULSE_WEB_TEST -AutorizarDDL
    Set-Stage 'Pruebas reales HTTPS contra Oracle'
    & (Join-Path $PSScriptRoot 'validar-oracle-http.ps1')
    Set-Stage 'Restricciones Oracle y rollback'
    & (Join-Path $PSScriptRoot 'validar-restricciones-oracle.ps1')
    Set-Stage 'Activar Oracle como proveedor del portal'
    & dotnet user-secrets set 'Database:Provider' 'Oracle' --project src/Finpulse.Web | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se activo la configuracion local.' }
    & (Join-Path $PSScriptRoot 'verificar-conexion-oracle.ps1') -Modo Web
    @{ Stage='Completado'; Success=$true; Provider='Oracle'; UpdatedAt=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    Write-Host 'Conexion y pruebas Oracle completadas. Puedes dejar abierta esta consola.'
} catch {
    $codes = [regex]::Matches($_.Exception.Message, '(?:ORA|SP2)-\d+') | ForEach-Object { $_.Value } | Select-Object -Unique
    @{ Stage=$stage; Success=$false; Failed=$true; Codes=@($codes); UpdatedAt=[DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statusPath -Encoding UTF8
    Write-Host ('No se completo la etapa: ' + $stage + '. Codigos: ' + ($codes -join ', '))
    Write-Host 'No repitas la instalacion completa. El diagnostico continuara con inventario de solo lectura.'
}
