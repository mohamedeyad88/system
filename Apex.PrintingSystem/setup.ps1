# Setup Solution
$ErrorActionPreference = "Stop"
$env:Path = "C:\Program Files\dotnet;" + $env:Path

if (!(Test-Path "Apex.PrintingSystem.sln")) {
    dotnet new sln -n Apex.PrintingSystem
}

# Create Projects
if (!(Test-Path "Apex.Core")) { dotnet new classlib -n Apex.Core }
if (!(Test-Path "Apex.Data")) { dotnet new classlib -n Apex.Data }
if (!(Test-Path "Apex.Services")) { dotnet new classlib -n Apex.Services }
if (!(Test-Path "Apex.UI")) { dotnet new wpf -n Apex.UI }

# Add to Solution
dotnet sln add Apex.Core/Apex.Core.csproj
dotnet sln add Apex.Data/Apex.Data.csproj
dotnet sln add Apex.Services/Apex.Services.csproj
dotnet sln add Apex.UI/Apex.UI.csproj

# Add References
dotnet add Apex.Data/Apex.Data.csproj reference Apex.Core/Apex.Core.csproj
dotnet add Apex.Services/Apex.Services.csproj reference Apex.Core/Apex.Core.csproj
dotnet add Apex.Services/Apex.Services.csproj reference Apex.Data/Apex.Data.csproj
dotnet add Apex.UI/Apex.UI.csproj reference Apex.Services/Apex.Services.csproj
dotnet add Apex.UI/Apex.UI.csproj reference Apex.Core/Apex.Core.csproj

# Add Packages
dotnet add Apex.UI/Apex.UI.csproj package Microsoft.Extensions.DependencyInjection
dotnet add Apex.UI/Apex.UI.csproj package Microsoft.Extensions.Hosting
dotnet add Apex.UI/Apex.UI.csproj package Serilog
dotnet add Apex.UI/Apex.UI.csproj package Serilog.Extensions.Logging
dotnet add Apex.UI/Apex.UI.csproj package Serilog.Sinks.File
dotnet add Apex.Data/Apex.Data.csproj package Microsoft.EntityFrameworkCore.SqlServer
dotnet add Apex.Data/Apex.Data.csproj package Microsoft.EntityFrameworkCore.Tools

Write-Host "Setup Complete"
