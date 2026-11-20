# Ejecutar localmente. Creacion/reanudacion requieren aprobacion y -AutorizarDDL.
# No imprime contrasenas ni cambia FINPULSE_APP. RestablecerClave solo admite FINPULSE_WEB vacio.
param(
    [switch]$CrearEsquemas,
    [switch]$ReanudarEsquemas,
    [switch]$AutorizarDDL,
    [switch]$RestablecerClave,
    [ValidateSet('FINPULSE_WEB','FINPULSE_WEB_TEST')][string[]]$Esquemas = @('FINPULSE_WEB','FINPULSE_WEB_TEST')
)
$ErrorActionPreference = 'Stop'
if ($CrearEsquemas -and $ReanudarEsquemas) { throw 'Selecciona creacion nueva o reanudacion; no ambas.' }
if (($CrearEsquemas -or $ReanudarEsquemas) -and -not $AutorizarDDL) { throw 'Requiere aprobacion administrativa previa y la opcion explicita -AutorizarDDL. No se ejecuto DDL.' }
if ($RestablecerClave -and (-not $ReanudarEsquemas -or -not $AutorizarDDL -or $Esquemas.Count -ne 1 -or $Esquemas[0] -ne 'FINPULSE_WEB')) { throw 'RestablecerClave solo admite reanudacion autorizada de FINPULSE_WEB vacio.' }
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Finpulse.Web\Finpulse.Web.csproj'
$resumeSchema = Join-Path $root 'sql\09_reanudar_esquema_web_oracle.sql'
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousOutputEncoding = $OutputEncoding
$previousNlsLang = $env:NLS_LANG

function Invoke-OracleSql([string]$Sql, [string[]]$Secrets = @(), [string]$Etapa = 'SQL') {
    $output = ($Sql | & sqlplus -L -S '/ as sysdba' 2>&1 | Out-String)
    $code = $LASTEXITCODE
    foreach ($secret in $Secrets) { if ($secret) { $output = $output.Replace($secret, '[REDACTADO]') } }
    # No output on success; even failed CONNECT statements must never expose credentials.
    if ($code -ne 0 -or $output -match '(?m)(ORA-|SP2-)') {
        $numbers = [regex]::Matches($output, '(?:ORA|SP2)-\d+') | ForEach-Object { $_.Value } | Select-Object -Unique
        $objects = [regex]::Matches($output, 'FALLO (?:TABLE|INDEX) [A-Za-z0-9_]+ SQLCODE=-?\d+') | ForEach-Object { $_.Value }
        throw ('Etapa ' + $Etapa + ': ' + ($numbers -join ', ') + '. ' + ($objects -join ', ') + '. DDL puede haberse confirmado parcialmente. Inventaria antes de reanudar; no recrees objetos.')
    }
    [regex]::Matches($output, '(?m)^(?:PLAN|APLICAR|CONSERVADA|CONSERVADO) (?:TABLE|INDEX) [A-Za-z0-9_]+\s*$') | ForEach-Object { Write-Host $_.Value.Trim() }
    $script:lastOraclePlan = @{ Tables = [regex]::Matches($output, '(?m)^PLAN TABLE ').Count; Indexes = [regex]::Matches($output, '(?m)^PLAN INDEX ').Count }
}
function Read-LocalPassword([string]$UserName) {
    $secure = Read-Host "Contrasena Oracle de $UserName (oculta)" -AsSecureString
    $ptr = [IntPtr]::Zero
    try {
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        $value = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
        if ([string]::IsNullOrWhiteSpace($value) -or $value -match '["\r\n\x00]') { throw 'La contrasena no puede estar vacia ni contener comillas dobles o saltos de linea.' }
        return $value
    } finally {
        if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
        $secure.Dispose()
    }
}
function New-Connection([string]$UserName, [string]$Password) {
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['User Id'] = $UserName
    $builder['Password'] = $Password
    $builder['Data Source'] = 'localhost:1521/FREEPDB1'
    $builder['Pooling'] = 'true'
    return $builder.ConnectionString
}

& dotnet build $project -c Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion.' }
if ($CrearEsquemas) {
    # Validate both names before creating either account. No changes to existing accounts.
    $selectedNames = "'" + ($Esquemas -join "','") + "'"
    Invoke-OracleSql @"
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE n NUMBER;
BEGIN
 SELECT COUNT(*) INTO n FROM DBA_USERS WHERE USERNAME IN ($selectedNames);
 IF n <> 0 THEN RAISE_APPLICATION_ERROR(-20003, 'Uno de los esquemas ya existe. Instalacion cancelada.'); END IF;
END;
/
EXIT
"@
}
$previousProvider = $env:Database__Provider
$previousConnection = $env:ConnectionStrings__Oracle
$previousExpected = $env:Oracle__ExpectedUser
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    $env:NLS_LANG = '.AL32UTF8'
    $passwords = @{}
    $connections = @{}
    $plans = @{}
    foreach ($name in $Esquemas) {
        if ($RestablecerClave) { Write-Host 'Elige una NUEVA contrasena para el esquema vacio FINPULSE_WEB.' }
        $passwords[$name] = Read-LocalPassword $name
        $connections[$name] = New-Connection $name $passwords[$name]
    }
    if ($RestablecerClave) {
        $newPassword = $passwords['FINPULSE_WEB']
        Invoke-OracleSql @"
SET ECHO OFF VERIFY OFF DEFINE OFF
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE n NUMBER;
BEGIN
 SELECT COUNT(*) INTO n FROM DBA_USERS WHERE USERNAME='FINPULSE_WEB';
 IF n<>1 THEN RAISE_APPLICATION_ERROR(-20014,'No existe FINPULSE_WEB.'); END IF;
 SELECT COUNT(*) INTO n FROM DBA_OBJECTS WHERE OWNER='FINPULSE_WEB';
 IF n<>0 THEN RAISE_APPLICATION_ERROR(-20015,'El esquema no esta vacio; no se cambia la contrasena.'); END IF;
END;
/
ALTER USER FINPULSE_WEB IDENTIFIED BY "$newPassword";
EXIT
"@ @($newPassword) 'RESTABLECER CLAVE DE ESQUEMA VACIO'
    }
    if ($ReanudarEsquemas) {
        # Preflight de todos los esquemas seleccionados antes de conceder permisos o crear objetos.
        # Una cuenta ausente exige una creacion separada; nunca se cambia una contrasena existente.
        foreach ($name in $Esquemas) {
            $password = $passwords[$name]
            Invoke-OracleSql @"
SET ECHO OFF VERIFY OFF DEFINE OFF
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE n NUMBER;
BEGIN
 SELECT COUNT(*) INTO n FROM DBA_USERS WHERE USERNAME='$name';
 IF n<>1 THEN RAISE_APPLICATION_ERROR(-20013,'Esquema ausente. Autorizar su creacion por separado.'); END IF;
END;
/
CONNECT $name/"$password"@localhost:1521/FREEPDB1
@"$resumeSchema" PLAN
EXIT
"@ @($passwords.Values) ('PLAN ' + $name)
            $plans[$name] = $script:lastOraclePlan
        }
    }
    if ($CrearEsquemas -or $ReanudarEsquemas) {
        foreach ($name in $Esquemas) {
            $password = $passwords[$name]
            $createStatement = ''
            if ($CrearEsquemas) { $createStatement = 'CREATE USER ' + $name + ' IDENTIFIED BY "' + $password + '" DEFAULT TABLESPACE USERS QUOTA 100M ON USERS;' }
            $needsTables = if ($CrearEsquemas -or $plans[$name].Tables -gt 0) { 1 } else { 0 }
            # Cada concesion se comprueba; no roles ni privilegios ANY. No se altera una cuenta existente.
            Invoke-OracleSql @"
SET ECHO OFF VERIFY OFF DEFINE OFF
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
ALTER SESSION SET CONTAINER = FREEPDB1;
$createStatement
DECLARE n NUMBER;
BEGIN
 FOR p IN (SELECT 'CREATE SESSION' privilege FROM DUAL UNION ALL SELECT 'CREATE TABLE' FROM DUAL UNION ALL SELECT 'CREATE SEQUENCE' FROM DUAL) LOOP
  SELECT COUNT(*) INTO n FROM DBA_SYS_PRIVS WHERE GRANTEE='$name' AND PRIVILEGE=p.privilege;
  IF n=0 AND (p.privilege='CREATE SESSION' OR $needsTables=1) THEN EXECUTE IMMEDIATE 'GRANT '||p.privilege||' TO $name'; END IF;
 END LOOP;
END;
/
CONNECT $name/"$password"@localhost:1521/FREEPDB1
@"$resumeSchema" APLICAR
CONNECT / AS SYSDBA
ALTER SESSION SET CONTAINER = FREEPDB1;
DECLARE n NUMBER;
BEGIN
 FOR p IN (SELECT 'CREATE TABLE' privilege FROM DUAL UNION ALL SELECT 'CREATE SEQUENCE' FROM DUAL) LOOP
  SELECT COUNT(*) INTO n FROM DBA_SYS_PRIVS WHERE GRANTEE='$name' AND PRIVILEGE=p.privilege;
  IF n<>0 THEN EXECUTE IMMEDIATE 'REVOKE '||p.privilege||' FROM $name'; END IF;
 END LOOP;
END;
/
EXIT
"@ @($passwords.Values) ('INSTALAR ' + $name)
        }
    }
    $env:Database__Provider = 'Oracle'
    foreach ($name in $Esquemas) {
        $env:ConnectionStrings__Oracle = $connections[$name]
        $env:Oracle__ExpectedUser = $name
        & dotnet run --project $project -c Release --no-build --no-launch-profile -- --verify-oracle
        if ($LASTEXITCODE -ne 0) { throw 'No se activo Oracle: fallo la verificacion real.' }
        & dotnet run --project $project -c Release --no-build --no-launch-profile -- --seed-categories
        if ($LASTEXITCODE -ne 0) { throw 'No se activo Oracle: fallo la carga de categorias.' }
    }
    # JSON enters via stdin, never via process arguments, console, Git or appsettings.json.
    $secrets = @{}
    if ($connections.ContainsKey('FINPULSE_WEB')) { $secrets['ConnectionStrings:Oracle'] = $connections['FINPULSE_WEB'] }
    if ($connections.ContainsKey('FINPULSE_WEB_TEST')) { $secrets['ConnectionStrings:OracleTest'] = $connections['FINPULSE_WEB_TEST'] }
    if ($connections.ContainsKey('FINPULSE_WEB') -and $connections.ContainsKey('FINPULSE_WEB_TEST')) { $secrets['Database:Provider'] = 'Oracle' }
    $secrets | ConvertTo-Json -Compress | & dotnet user-secrets set --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se guardo la configuracion local.' }
    $statusDirectory = Join-Path $root 'tests\.qa'
    New-Item -ItemType Directory -Force -Path $statusDirectory | Out-Null
    @{ Success = $true; Schemas = $Esquemas; Activated = $secrets.ContainsKey('Database:Provider'); Container = 'FREEPDB1'; CheckedAt = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $statusDirectory 'oracle-setup.json') -Encoding UTF8
    Write-Output ('Conexiones verificadas y guardadas en User Secrets: ' + ($Esquemas -join ', ') + '. Oracle solo se activa si se verifican ambos esquemas en esta ejecucion.')
    Write-Output 'Solo se inicializaron categorias. Ejecuta scripts/validar-oracle-web.ps1 para comprobar los flujos web.'
} finally {
    $env:Database__Provider = $previousProvider
    $env:ConnectionStrings__Oracle = $previousConnection
    $env:Oracle__ExpectedUser = $previousExpected
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $OutputEncoding = $previousOutputEncoding
    $env:NLS_LANG = $previousNlsLang
    Remove-Variable passwords,connections,password,newPassword,createStatement,secrets,plans -ErrorAction SilentlyContinue
}
