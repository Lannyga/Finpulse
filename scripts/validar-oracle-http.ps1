$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
$previous=$env:ConnectionStrings__Oracle
$previousCertificate=$env:FINPULSE_QA_CERTIFICATE
try {
    $secretPath=Join-Path $env:APPDATA 'Microsoft\UserSecrets\finpulse-web-local\secrets.json'
    $settings=Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
    $env:ConnectionStrings__Oracle=$settings.'ConnectionStrings:OracleTest'
    if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Oracle)) { throw 'Falta la conexion OracleTest local.' }
    $certificateDirectory=Join-Path $root ('tests\.qa\oracle-certificate-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null
    $rsa=[Security.Cryptography.RSACryptoServiceProvider]::new(2048)
    $rsa.set_PersistKeyInCsp($false)
    try {
        $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $san.AddDnsName('localhost')
        $request.CertificateExtensions.Add($san.Build())
        $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(1))
        try {
            $env:FINPULSE_QA_CERTIFICATE=Join-Path $certificateDirectory 'https.pfx'
            [IO.File]::WriteAllBytes($env:FINPULSE_QA_CERTIFICATE,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx))
        } finally { $certificate.Dispose() }
    } finally { $rsa.Dispose() }
    & node scripts/validar-oracle-http.cjs | Tee-Object -FilePath docs/qa/web-oracle-http-checks.log
    if ($LASTEXITCODE -ne 0) { throw 'No se completaron las comprobaciones HTTPS Oracle.' }
} finally {
    $env:ConnectionStrings__Oracle=$previous
    $env:FINPULSE_QA_CERTIFICATE=$previousCertificate
    Remove-Variable settings -ErrorAction SilentlyContinue
    Pop-Location
}
