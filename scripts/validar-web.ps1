param([int]$Port = 7250, [switch]$Snapshots)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $projectRoot
dotnet build FINPULSE.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
$checks = Join-Path $projectRoot 'tests\Finpulse.WebChecks\bin\Release\net10.0\Finpulse.WebChecks.dll'
$web = Join-Path $projectRoot 'src\Finpulse.Web\bin\Release\net10.0\Finpulse.Web.dll'
dotnet $checks | Tee-Object -FilePath docs\qa\web-data-checks.log
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de datos.' }
$qaDirectory = Join-Path $projectRoot ('tests\.qa\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qaDirectory -Force | Out-Null
$certificate = Join-Path $qaDirectory 'https.pfx'
dotnet $checks --certificate $certificate
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el certificado de pruebas.' }
$env:Database__Provider = 'Sqlite'
$env:ConnectionStrings__Sqlite = 'Data Source=' + (Join-Path $qaDirectory 'data.db') + ';Foreign Keys=True;Pooling=False'
$env:DataProtection__KeyDirectory = Join-Path $qaDirectory 'keys'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'https://localhost:' + $Port
$env:Kestrel__Certificates__Default__Path = $certificate
Push-Location (Join-Path $projectRoot 'src\Finpulse.Web')
try { dotnet $web --init-local; if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la base aislada.' } } finally { Pop-Location }
$server = Start-Process -FilePath 'dotnet' -ArgumentList ('"' + $web + '"') -WorkingDirectory (Join-Path $projectRoot 'src\Finpulse.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $qaDirectory 'server.log') -RedirectStandardError (Join-Path $qaDirectory 'error.log')
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($server.HasExited) { throw 'El servidor de pruebas terminó antes de iniciar.' }
        $socket = New-Object System.Net.Sockets.TcpClient
        try { $socket.Connect('localhost', $Port); $ready = $true } catch { Start-Sleep -Milliseconds 500 } finally { $socket.Dispose() }
        if ($ready) { break }
    }
    if (-not $ready) { throw 'El servidor de pruebas no abrió el puerto.' }
    if ($Snapshots) { $env:FINPULSE_QA_SNAPSHOTS = Join-Path $projectRoot 'docs\qa\web-preview' }
    dotnet $checks --http ('https://localhost:' + $Port) | Tee-Object -FilePath docs\qa\web-http-checks.log
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron los flujos HTTP.' }
} finally {
    if (-not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
