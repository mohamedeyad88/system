$ErrorActionPreference = "Stop"

# Define paths
$cliProject = "..\Apex.NumberedBooksEngine.CLI\Apex.NumberedBooksEngine.CLI.csproj"
$templatePath = "..\Apex.NumberedBooksEngine.CLI\samples\template.png"
$slotsPath = "..\Apex.NumberedBooksEngine.CLI\samples\slots.json"
$outputPath = "stress_test_output.pdf"

# Stress Test Parameters
$totalNumbers = 10000
$startNumber = 1
$copies = 1

Write-Host "Starting Stress Test..."
Write-Host "Total Numbers: $totalNumbers"
Write-Host "Copies: $copies"

$startTime = Get-Date

# Run CLI
dotnet run --project $cliProject --configuration Release -- generate `
    --template $templatePath `
    --slots $slotsPath `
    --out $outputPath `
    --start $startNumber `
    --total $totalNumbers `
    --copies $copies `
    --mode Auto

$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host "Stress Test Completed!"
Write-Host "Duration: $($duration.TotalSeconds) seconds"

if (Test-Path $outputPath) {
    $size = (Get-Item $outputPath).Length / 1MB
    Write-Host "Output File Size: $($size.ToString("F2")) MB"
} else {
    Write-Error "Output file not found!"
}
