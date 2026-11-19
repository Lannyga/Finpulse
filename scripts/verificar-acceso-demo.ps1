param([string]$BaseUrl = 'https://localhost:7240', [bool]$ExpectAvailable = $true)
$ErrorActionPreference = 'Stop'
$origin = [uri]$BaseUrl
if (-not $origin.IsLoopback -or $origin.Scheme -ne 'https') { throw 'La verificacion requiere HTTPS local.' }
$checks = 0
function Assert-Access([bool]$condition, [string]$label) {
    if (-not $condition) { throw $label }
    $script:checks++
    Write-Output ('OK ' + $script:checks + ': ' + $label)
}
function Get-Token($html) {
    [Net.WebUtility]::HtmlDecode([regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value)
}
function Get-Status([string]$path, [string]$method = 'Get', $body = $null) {
    try { $r = Invoke-WebRequest ($BaseUrl + $path) -UseBasicParsing -WebSession $script:session -Method $method -Body $body; return [int]$r.StatusCode }
    catch [System.Net.WebException] { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }; throw }
}
$login = Invoke-WebRequest ($BaseUrl + '/Account/Login') -UseBasicParsing -SessionVariable session
Assert-Access ($login.StatusCode -eq 200) 'Inicio de sesion responde'
Assert-Access ($login.Content.Contains('action="/Account/DemoLogin"') -eq $ExpectAvailable) 'Visibilidad del acceso demo segun entorno y cuenta'
Assert-Access (-not $login.Content.Contains('Demo!')) 'No se publica la contrasena demo en HTML'
Assert-Access ((Get-Status '/Account/DemoLogin') -eq 405) 'DemoLogin no permite GET'
Assert-Access ((Get-Status '/Account/DemoLogin' 'Post' @{}) -eq 400) 'Acceso demo requiere antiforgery'
$token = Get-Token $login.Content
if (-not $ExpectAvailable) {
    Assert-Access ((Get-Status '/Account/DemoLogin' 'Post' @{__RequestVerificationToken=$token}) -eq 404) 'Acceso demo deshabilitado en este entorno'
    return
}
$dashboard = Invoke-WebRequest ($BaseUrl + '/Account/DemoLogin') -UseBasicParsing -WebSession $session -Method Post -Body @{__RequestVerificationToken=$token; Username='otro_usuario'; id='1'}
Assert-Access (($dashboard.BaseResponse.ResponseUri.AbsolutePath -in @('/','/Finance/Index')) -and $dashboard.Content.Contains('Resumen')) 'Un clic abre el dashboard'
Assert-Access ($dashboard.Content.Contains('demo_6meses_9daf6d1f')) 'Solo accede a la cuenta demo configurada, ignorando IDs enviados'
Assert-Access ($dashboard.Content.Contains('session-logout')) 'Cerrar sesion esta en la cabecera fuera de la barra lateral'
Assert-Access ((Get-Status '/Account/Logout') -eq 405) 'Cerrar sesion no permite GET'
Assert-Access ((Get-Status '/Account/Logout' 'Post' @{}) -eq 400) 'Cerrar sesion requiere antiforgery'
$signedOut = Invoke-WebRequest ($BaseUrl + '/Account/Logout') -UseBasicParsing -WebSession $session -Method Post -Body @{__RequestVerificationToken=(Get-Token $dashboard.Content)}
Assert-Access ($signedOut.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login') 'Cerrar sesion vuelve al acceso'
$private = Invoke-WebRequest ($BaseUrl + '/Finance/Index') -UseBasicParsing -WebSession $session
Assert-Access ($private.BaseResponse.ResponseUri.AbsolutePath -eq '/Account/Login') 'Sesion cerrada pierde acceso a finanzas'
$again = Invoke-WebRequest ($BaseUrl + '/Account/DemoLogin') -UseBasicParsing -WebSession $session -Method Post -Body @{__RequestVerificationToken=(Get-Token $private.Content)}
Assert-Access ($again.Content.Contains('demo_6meses_9daf6d1f')) 'Es posible volver a entrar a demo con un clic'
Write-Output ('Resultado: ' + $checks + ' comprobaciones aprobadas.')
