param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
try {
    foreach ($abi in @('armeabi-v7a', 'arm64-v8a', 'x86_64')) {
        if (-not ($archive.Entries | Where-Object FullName -eq "lib/$abi/libmonosgen-2.0.so")) {
            throw "Missing .NET runtime for $abi in $Path"
        }
    }
    Write-Host 'APK contains ARM32, ARM64 and x64 runtimes.'
} finally { $archive.Dispose() }
