# Verifica el segundo recorrido del instalador, sin volver a conceder permisos ni cambiar objetos.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$settings=Get-Content (Join-Path $env:APPDATA 'Microsoft\UserSecrets\finpulse-web-local\secrets.json') -Raw | ConvertFrom-Json
$previousEncoding=$OutputEncoding
$previousNls=$env:NLS_LANG
$logs=@()
try {
    $OutputEncoding=[Text.UTF8Encoding]::new($false)
    $env:NLS_LANG='.AL32UTF8'
    foreach($key in @('ConnectionStrings:Oracle','ConnectionStrings:OracleTest')) {
        $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($settings.$key)
        $name=[string]$builder['User Id']
        $password=[string]$builder['Password']
        if($name -notin @('FINPULSE_WEB','FINPULSE_WEB_TEST') -or $builder['Data Source'] -ne 'localhost:1521/FREEPDB1') { throw 'Conexion fuera del alcance autorizado.' }
        $schema=Join-Path $root 'sql\09_reanudar_esquema_web_oracle.sql'
        foreach($mode in @('PLAN','APLICAR')) {
            $sql=@"
SET ECHO OFF VERIFY OFF DEFINE OFF
WHENEVER SQLERROR EXIT SQL.SQLCODE
CONNECT $name/"$password"@localhost:1521/FREEPDB1
@"$schema" $mode
EXIT
"@
            $raw=($sql | & sqlplus -L -S /nolog 2>&1 | Out-String)
            $code=$LASTEXITCODE
            $safe=$raw.Replace($password,'[REDACTADO]')
            $logs+=$safe
            if($code -ne 0 -or $safe -match '(?:ORA|SP2)-\d+' -or $safe -match '(?m)^(?:PLAN|APLICAR) (?:TABLE|INDEX) ') {
                $logs | Set-Content (Join-Path $root 'docs\qa\web-oracle-idempotence.log') -Encoding UTF8
                throw ('No se confirmo la reanudacion sin cambios: '+$name+' '+$mode)
            }
        }
        Write-Output ('OK: '+$name+' PLAN y APLICAR conservaron las seis tablas y todos los indices. Sin nuevas concesiones ni DDL.')
    }
    $logs | Set-Content (Join-Path $root 'docs\qa\web-oracle-idempotence.log') -Encoding UTF8
} finally {
    $OutputEncoding=$previousEncoding
    $env:NLS_LANG=$previousNls
    Remove-Variable settings,builder,password,sql,raw -ErrorAction SilentlyContinue
}
