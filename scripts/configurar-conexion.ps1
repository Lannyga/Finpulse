# Ejecutar en PowerShell. Solicita contraseña sin mostrarla en pantalla.
$usuario = 'FINPULSE_APP'
$servicio = 'localhost:1521/FREEPDB1'
$segura = Read-Host 'Contraseña de FINPULSE_APP' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura)
try {
    $clave = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['User Id'] = $usuario
    $builder['Password'] = $clave
    $builder['Data Source'] = $servicio
    $builder['Pooling'] = 'true'
    $conexion = $builder.ConnectionString
    [Environment]::SetEnvironmentVariable('FINPULSE_ORACLE_CONNECTION', $conexion, 'User')
    $env:FINPULSE_ORACLE_CONNECTION = $conexion
    Write-Host 'Conexión configurada para el usuario actual. Reinicia Visual Studio.'
} finally {
    if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    Remove-Variable clave, conexion -ErrorAction SilentlyContinue
}
# Aviso: la variable de entorno del usuario no equivale a un almacén seguro de secretos.
# Antes de distribuir el software, migrar credenciales a Windows Credential Manager
# o a una arquitectura con servicio intermedio y cuentas individuales.
