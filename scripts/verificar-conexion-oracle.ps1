# Diagnóstico SELECT de solo lectura con ODP.NET, sin crear usuarios ni modificar datos.
param(
    [ValidateSet('Admin','App','Web')][string]$Modo = 'Web',
    [switch]$SolicitarClave
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Modo -eq 'Web') {
    if ($SolicitarClave) { throw 'Para entrada segura utiliza scripts/configurar-oracle-web.ps1; este diagnostico web es de solo lectura.' }
    $previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        & dotnet run --project (Join-Path $projectRoot 'src\Finpulse.Web') --no-launch-profile -- --verify-oracle
        if ($LASTEXITCODE -ne 0) { throw 'No se verifico la conexion Oracle del portal.' }
    } finally { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment }
    return
}
# Admin y App se conservan exclusivamente para diagnosticar la implementacion anterior.
$project = Join-Path $projectRoot 'tests\Finpulse.Smoke\Finpulse.Smoke.csproj'
$reportPath = Join-Path $projectRoot ('docs\oracle-diagnostico-' + $Modo.ToLowerInvariant() + '.json')
$previousDiagnostic = $env:FINPULSE_ORACLE_DIAGNOSTIC_CONNECTION
$previousExpected = $env:FINPULSE_ORACLE_EXPECTED_USER
$ptr = [IntPtr]::Zero
try {
    # Compilar antes de solicitar secretos. stdout queda separado del resultado JSON.
    & dotnet build $project --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el diagnóstico.' }
    $expectedUser = if ($Modo -eq 'Admin') { 'SYSTEM' } else { 'FINPULSE_APP' }
    $connection = $null
    if ($Modo -eq 'App' -and -not $SolicitarClave) {
        $connection = [Environment]::GetEnvironmentVariable('FINPULSE_ORACLE_CONNECTION','Process')
        if ([string]::IsNullOrWhiteSpace($connection)) {
            $connection = [Environment]::GetEnvironmentVariable('FINPULSE_ORACLE_CONNECTION','User')
        }
    }
    if ([string]::IsNullOrWhiteSpace($connection)) {
        $securePassword = Read-Host "Contrasena de $expectedUser (entrada oculta, no se guarda)" -AsSecureString
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder['User Id'] = $expectedUser
        $builder['Password'] = $password
        $builder['Data Source'] = 'localhost:1521/FREEPDB1'
        $connection = $builder.ConnectionString
    }
    # Exclusivamente entorno del proceso hijo; SYSTEM nunca se asigna a la variable de la app.
    $env:FINPULSE_ORACLE_DIAGNOSTIC_CONNECTION = $connection
    $env:FINPULSE_ORACLE_EXPECTED_USER = $expectedUser
    $raw = & dotnet run --project $project --no-build -- --oracle-connectivity
    $exitCode = $LASTEXITCODE
    $result = ($raw -join [Environment]::NewLine) | ConvertFrom-Json
    # Lista explícita de campos permitidos. No guardar salida arbitraria del proceso.
    $safeReport = [ordered]@{
        CheckedAt = [DateTimeOffset]::UtcNow.ToString('o')
        Success = [bool]$result.Success
        Status = $result.Status
        Message = $result.Message
        User = $result.User
        Container = $result.Container
        OracleError = $result.OracleError
        FinpulseExists = $result.FinpulseExists
        FinpulseAppExists = $result.FinpulseAppExists
        UsersTableExists = $result.UsersTableExists
    }
    $safeReport | ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $safeReport | ConvertTo-Json | Write-Output
    if ($exitCode -ne 0) { throw 'La conexion no se verifico. Consulta el estado y codigo ORA del informe sin secretos.' }
} finally {
    $env:FINPULSE_ORACLE_DIAGNOSTIC_CONNECTION = $previousDiagnostic
    $env:FINPULSE_ORACLE_EXPECTED_USER = $previousExpected
    if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    if ($null -ne $securePassword) { $securePassword.Dispose() }
    Remove-Variable password,connection,builder,raw,securePassword -ErrorAction SilentlyContinue
}
