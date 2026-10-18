param([int]$Port = 7543, [string]$NamePattern = '')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $repo
$dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { (Get-Command dotnet).Source }
$fixture = Join-Path $repo ("test-results/browser-fixture/" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $fixture | Out-Null
$hostProcess = $null
try {
    & (Join-Path $repo 'tests/native/create-certificate.ps1')
    & npm.cmd run assets
    if ($LASTEXITCODE) { throw 'Building JavaScript assets failed.' }
    & $dotnet build tests/DigitalSorter.BrowserHost -c Release -p:RestoreLockedMode=true --verbosity quiet
    if ($LASTEXITCODE) { throw 'Browser host build failed.' }
    & $dotnet publish src/DigitalSorter.Auth -c Release --no-restore -o "$fixture/published" --verbosity quiet
    if ($LASTEXITCODE) { throw 'Web publish failed.' }
    $env:TEST_PORT = "$Port"
    $env:TEST_ORIGIN = "https://localhost:$Port"
    $env:TEST_BIND_ADDRESS = '127.0.0.1'
    $env:TEST_CERTIFICATE = Join-Path $repo 'test-results/native-fixture/test.pfx'
    $env:TEST_CERTIFICATE_PEM = Join-Path $repo 'test-results/native-fixture/test-ca.pem'
    $env:TEST_CERTIFICATE_PASSWORD = ''
    $env:TEST_WEB_ROOT = Join-Path $fixture 'published/wwwroot'
    $env:TEST_FIXTURES = Join-Path $repo 'tests/fixtures'
    $env:TEST_DATA_DIRECTORY = Join-Path $fixture 'data'
    $dll = Join-Path $repo 'tests/DigitalSorter.BrowserHost/bin/Release/net10.0/DigitalSorter.BrowserHost.dll'
    $hostProcess = Start-Process $dotnet -ArgumentList @('"' + $dll + '"') -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput "$fixture/host.log" -RedirectStandardError "$fixture/error.log"
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($hostProcess.HasExited) { throw "Browser host exited. See $fixture/host.log and error.log" }
        $probe = [System.Net.Sockets.TcpClient]::new()
        try {
            $probe.Connect('127.0.0.1', $Port)
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 500 } finally { $probe.Dispose() }
    }
    if (-not $ready) { throw 'Browser test host did not become ready.' }
    $nodeArguments = @('--test', '--test-concurrency=1')
    if ($NamePattern) { $nodeArguments += "--test-name-pattern=$NamePattern" }
    & node @nodeArguments tests/browser/gallery.spec.mjs tests/browser/media-worker.spec.mjs
    if ($LASTEXITCODE) { throw 'Browser tests failed.' }
} finally {
    if ($hostProcess -and -not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id; $hostProcess.WaitForExit() }
    Get-ChildItem Env:TEST_* | Remove-Item
}
