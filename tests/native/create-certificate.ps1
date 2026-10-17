$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot '../../test-results/native-fixture'
New-Item -ItemType Directory -Force $output | Out-Null
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=DigitalSorter controlled native tests', $rsa,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256,
    [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
$san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$san.AddDnsName('localhost')
$san.AddIpAddress([System.Net.IPAddress]::Parse('127.0.0.1'))
$san.AddIpAddress([System.Net.IPAddress]::Parse('10.0.2.2'))
$request.CertificateExtensions.Add($san.Build())
$request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
$certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(2))
[System.IO.File]::WriteAllText((Join-Path $output 'test-ca.pem'), $certificate.ExportCertificatePem())
[System.IO.File]::WriteAllBytes((Join-Path $output 'test.pfx'), $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, ''))
$certificate.Dispose()
$rsa.Dispose()
