# Apex Printing System - Developer Guide

## Overview
Apex Printing System is a WPF-based print management solution built on .NET 8. It uses MVVM (CommunityToolkit), Dependency Injection (Microsoft.Extensions), and Entity Framework Core (SQLite).

## Getting Started

1.  **Prerequisites**:
    -   Visual Studio 2022
    -   .NET 8 SDK
    -   PowerShell 7+

2.  **Build**:
    ```powershell
    dotnet build
    ```

3.  **Run**:
    ```powershell
    dotnet run --project Apex.UI
    ```

## Architecture
-   **Apex.Core**: Interfaces and Domain Models.
-   **Apex.Data**: EF Core Context and Repositories.
-   **Apex.Services**: Business Logic (Logging, Printing, Health).
-   **Apex.UI**: WPF Views and ViewModels.

## Key Services
-   **Logging**: `FileLoggerService` (Async, Thread-safe).
-   **Database**: `DatabaseHealthService` (Auto-backup/repair).
-   **Printing**: `PrintJobProcessor` (Channel-based queue).

## Deployment
Run `publish_clean.ps1` to generate a clean release build in `Publish/`.

## Testing
Run `dotnet test` to execute unit tests.
