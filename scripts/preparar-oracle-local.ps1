# Primera instalación local. Requiere autenticación Windows como SYSDBA.
# No reemplaza un usuario existente ni imprime la contraseña generada.
param([switch]$ResumeEmptySchema)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$schemaPath = Join-Path $projectRoot 'sql\02_esquema_usuarios.sql'
$passwordBytes = New-Object byte[] 24
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($passwordBytes) } finally { $rng.Dispose() }
$dbPassword = 'Fp9_' + [Convert]::ToBase64String($passwordBytes).Replace('+','A').Replace('/','B').Replace('=','C')
$createAccount = "CREATE USER FINPULSE_APP IDENTIFIED BY `"$dbPassword`" DEFAULT TABLESPACE USERS QUOTA 100M ON USERS;"
if ($ResumeEmptySchema) {
    # Recuperación explícita de una instalación fallida; nunca reutilizar un esquema con objetos.
    $createAccount = @"
DECLARE n NUMBER;
BEGIN
  SELECT COUNT(*) INTO n FROM DBA_OBJECTS WHERE OWNER = 'FINPULSE_APP';
  IF n <> 0 THEN RAISE_APPLICATION_ERROR(-20001, 'El esquema no esta vacio; recuperacion cancelada.'); END IF;
  EXECUTE IMMEDIATE 'ALTER USER FINPULSE_APP IDENTIFIED BY "$dbPassword"';
END;
/
"@
}
$sql = @"
SET ECHO OFF VERIFY OFF FEEDBACK OFF
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = FREEPDB1;
$createAccount
GRANT CREATE SESSION, CREATE TABLE, CREATE SEQUENCE TO FINPULSE_APP;
CONNECT FINPULSE_APP/"$dbPassword"@localhost:1521/FREEPDB1
@"$schemaPath"
SELECT COUNT(*) AS TABLA_VERIFICADA FROM USER_TABLES WHERE TABLE_NAME = 'FP_USUARIOS';
EXIT
"@
try {
    $output = $sql | & sqlplus -L -S '/ as sysdba' 2>&1
    # SQLPlus puede repetir la sentencia fallida: censurar la clave antes de mostrar salida.
    $safeOutput = ($output | Out-String).Replace($dbPassword, '[REDACTADO]')
    Write-Output $safeOutput
    if ($LASTEXITCODE -ne 0) { throw 'Oracle no pudo completar la preparación. No se configuró la conexión.' }
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['User Id'] = 'FINPULSE_APP'
    $builder['Password'] = $dbPassword
    $builder['Data Source'] = 'localhost:1521/FREEPDB1'
    $builder['Pooling'] = 'true'
    [Environment]::SetEnvironmentVariable('FINPULSE_ORACLE_CONNECTION', $builder.ConnectionString, 'User')
    $env:FINPULSE_ORACLE_CONNECTION = $builder.ConnectionString
    Write-Output 'Esquema local y conexión de usuario configurados. Reinicia la terminal o VS Code antes de ejecutar WPF.'
} finally {
    Remove-Variable dbPassword, createAccount, sql, output, safeOutput, builder -ErrorAction SilentlyContinue
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
}
