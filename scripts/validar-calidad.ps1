param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SinVistas,
    [switch]$RevisarVulnerabilidades
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logFolder = Join-Path $projectRoot 'docs\qa'
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
Push-Location -LiteralPath $projectRoot
try {
    function Invoke-DotnetGate {
        param([string]$Label, [string[]]$Arguments, [string]$LogName)
        $LogName = $LogName -replace '^07-', ('07-' + $Configuration + '-')
        Write-Host $Label
        $previousErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $lines = & dotnet @Arguments 2>&1
            $resultCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $previousErrorPreference }
        $lines | Tee-Object -FilePath (Join-Path $logFolder $LogName) | Out-Host
        if ($resultCode -ne 0) { throw "$Label terminó con código $resultCode. Consulta docs/qa/$LogName." }
    }
    # Build and execution are sequential: a running test process can lock its assemblies.
    Invoke-DotnetGate 'Restauración' @('restore', 'tests/Finpulse.Smoke/Finpulse.Smoke.csproj') '07-restore.log'
    Invoke-DotnetGate 'Compilación estricta' @('build', 'tests/Finpulse.Smoke/Finpulse.Smoke.csproj', '-c', $Configuration, '--no-restore', '-warnaserror') '07-build.log'
    Invoke-DotnetGate 'Reglas, cálculos, autenticación y persistencia' @('run', '--project', 'tests/Finpulse.Smoke/Finpulse.Smoke.csproj', '-c', $Configuration, '--no-build') '07-tests.log'
    if (-not $SinVistas) {
        Invoke-DotnetGate 'Vistas WPF y comandos con SQLite temporal' @('run', '--project', 'tests/Finpulse.Smoke/Finpulse.Smoke.csproj', '-c', $Configuration, '--no-build', '--', '--ui') '07-ui.log'
    }
    if ($RevisarVulnerabilidades) {
        Invoke-DotnetGate 'Consulta de vulnerabilidades NuGet' @('list', 'src/Finpulse.Infrastructure/Finpulse.Infrastructure.csproj', 'package', '--vulnerable', '--include-transitive', '--no-restore', '--format', 'json', '--output-version', '1') '07-dependencies.json'
        $report = Get-Content -Raw -LiteralPath (Join-Path $logFolder ('07-' + $Configuration + '-dependencies.json')) | ConvertFrom-Json
        $vulnerable = @(foreach ($project in $report.projects) {
            foreach ($framework in $project.frameworks) {
                foreach ($package in (@($framework.topLevelPackages) + @($framework.transitivePackages))) {
                    if ($null -ne $package -and $null -ne $package.vulnerabilities -and @($package.vulnerabilities).Count -gt 0) { $package.id }
                }
            }
        })
        if ($vulnerable.Count -gt 0) { throw ('NuGet reportó paquetes vulnerables: ' + ($vulnerable -join ', ')) }
    }
    Write-Host 'Validación finalizada: todas las etapas seleccionadas terminaron con código 0.'
}
finally { Pop-Location }
