Write-Host "--- CLEANING UP ---"
if (Test-Path "IntegrationPackage") { Remove-Item "IntegrationPackage" -Recurse -Force }
if (Test-Path "Run_Panel") { Remove-Item "Run_Panel" -Recurse -Force }

# Clean build artifacts
Get-ChildItem -Recurse -Include bin, obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "--- BUILDING SOLUTION ---"
dotnet restore "PipedStatusProject.sln"
dotnet build "PipedStatusProject.sln" --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build Failed!"
    exit 1
}

Write-Host "--- PACKAGING INTEGRATION LIB ---"
$libDest = "IntegrationPackage"
New-Item -ItemType Directory -Path $libDest | Out-Null
$libSource = "Shared\BitParser\bin\Release"
Copy-Item "$libSource\*.dll" $libDest
Copy-Item "$libSource\README_INTEGRATION.txt" $libDest -ErrorAction SilentlyContinue 

# Create README if missing
if (-not (Test-Path "$libDest\README.txt")) {
    $readmeContent = @"
FAST INTEGRATION GUIDE
======================
1. Add reference to BitParser.dll
2. Use PipeBridge:
   var bridge = new BitParser.PipeBridge();
   bridge.StartPcapServer("BitStatusPipe", "udp port 5000", "Ethernet", true); // true = SharedMemory
   bridge.Stop();
"@
    Set-Content "$libDest\README.txt" $readmeContent
}

Write-Host "--- DEPLOYING PANEL APP ---"
$appDest = "Run_Panel"
New-Item -ItemType Directory -Path $appDest | Out-Null
$appSource = "BitStatusPanel\BitStatusPanel\bin\Release"
Copy-Item "$appSource\*" $appDest -Recurse

Write-Host "--- DONE ---"
Write-Host "Library:  $PWD\IntegrationPackage"
Write-Host "App:      $PWD\Run_Panel\BitStatusPanel.exe"
