$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$report=Get-Content (Join-Path $root 'docs\qa\web-oracle-http-result.json') -Raw | ConvertFrom-Json
if (-not $report.Success -or $report.Username -notmatch '^qa_[a-f0-9]{12}$') { throw 'Falta una ejecucion HTTPS sintetica verificada.' }
$previousEncoding=$OutputEncoding
$previousNls=$env:NLS_LANG
try {
    $settings=Get-Content (Join-Path $env:APPDATA 'Microsoft\UserSecrets\finpulse-web-local\secrets.json') -Raw | ConvertFrom-Json
    $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($settings.'ConnectionStrings:OracleTest')
    if ($builder['User Id'] -ne 'FINPULSE_WEB_TEST' -or $builder['Data Source'] -ne 'localhost:1521/FREEPDB1') { throw 'Solo se permite la conexion aislada de pruebas.' }
    $password=[string]$builder['Password']
    if ($password -match '["\r\n\x00]') { throw 'Formato de clave no admitido para SQLPlus seguro.' }
    $OutputEncoding=[Text.UTF8Encoding]::new($false)
    $env:NLS_LANG='.AL32UTF8'
    $testSql=Join-Path $root 'sql\12_verificar_restricciones_web.sql'
    $name=$report.Username
    $sql=@"
SET ECHO OFF VERIFY OFF DEFINE OFF
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK
CONNECT FINPULSE_WEB_TEST/"$password"@localhost:1521/FREEPDB1
@"$testSql" $name
EXIT
"@
    $raw=($sql | & sqlplus -L -S /nolog 2>&1 | Out-String)
    $code=$LASTEXITCODE
    $safe=$raw.Replace($password,'[REDACTADO]')
    $safe | Set-Content (Join-Path $root 'docs\qa\web-oracle-constraints.log') -Encoding UTF8
    if ($code -ne 0 -or $safe -match '(?:ORA|SP2)-\d+') {
        $numbers=[regex]::Matches($safe,'(?:ORA|SP2)-\d+') | ForEach-Object { $_.Value } | Select-Object -Unique
        throw ('Fallaron las comprobaciones de restricciones: '+($numbers -join ', '))
    }
    Write-Output $safe
} finally {
    $OutputEncoding=$previousEncoding
    $env:NLS_LANG=$previousNls
    Remove-Variable settings,builder,password,sql,raw -ErrorAction SilentlyContinue
}
