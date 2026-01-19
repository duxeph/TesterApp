$source = "Shared\BitParser\bin\Release"
$dest = "IntegrationPackage"

if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
New-Item -ItemType Directory -Path $dest | Out-Null

$files = @(
    "BitParser.dll",
    "PcapDotNet.Base.dll",
    "PcapDotNet.Core.dll",
    "PcapDotNet.Packets.dll",
    "PcapDotNet.Core.Extensions.dll",
    "PacketDotNet.dll",
    "SharpPcap.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Text.Encoding.CodePages.dll"
)

foreach ($file in $files) {
    $srcPath = Join-Path $source $file
    if (Test-Path $srcPath) {
        Copy-Item $srcPath $dest
        Write-Host "Copied $file"
    } else {
        Write-Warning "Missing dependency: $file (This is fine if not required by Pcap choice)"
    }
}

Write-Host "Package created at $dest"
