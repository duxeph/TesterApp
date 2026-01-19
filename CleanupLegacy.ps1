# CleanupLegacy.ps1
# Removes all legacy/unused folders and files

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Cleaning Up Legacy Projects" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

$itemsToDelete = @(
    "PipeListener",
    "BitStatusPanel", 
    "EthernetPerfTester",
    "MasterLauncher",
    "IntegrationPackage",
    "Run_Panel",
    "Run_Source",
    "Run_Viewer",
    "_archive",
    "CopyPanel.ps1",
    "SetupBenchmark.ps1",
    "PHASE1_COMPLETE.md",
    "UPGRADES_COMPLETE.md",
    "CODE_REVIEW.md"
)

Write-Host "Items to be deleted:" -ForegroundColor Yellow
foreach ($item in $itemsToDelete) {
    if (Test-Path $item) {
        Write-Host "  [X] $item" -ForegroundColor Red
    }
    else {
        Write-Host "  [ ] $item (not found)" -ForegroundColor Gray
    }
}

Write-Host ""
$confirm = Read-Host "Proceed with deletion? (yes/no)"

if ($confirm -ne "yes") {
    Write-Host "Cleanup cancelled." -ForegroundColor Yellow
    exit
}

Write-Host ""
Write-Host "Deleting..." -ForegroundColor Yellow

foreach ($item in $itemsToDelete) {
    if (Test-Path $item) {
        Remove-Item -Path $item -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Deleted: $item" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Cleanup Complete!" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Remaining structure:" -ForegroundColor Cyan
Write-Host "  Shared/BitParser     (Core library)" -ForegroundColor Green
Write-Host "  UnifiedConsole       (Main app)" -ForegroundColor Green
Write-Host "  *.xml                (Schemas)" -ForegroundColor Green
Write-Host "  *.sln                (Solution file)" -ForegroundColor Green
Write-Host ""
