# Deployment Guide

## Prerequisites
-   Windows 10/11 (x64)
-   .NET 8 Runtime
-   SQLite (Embedded)

## Installation Steps

1.  **Publish**:
    Run the `publish_clean.ps1` script to generate the release build.
    ```powershell
    .\publish_clean.ps1
    ```

2.  **Deploy**:
    Copy the contents of `Publish/` to the target machine (e.g., `C:\Program Files\ApexPrintingSystem`).

3.  **First Run**:
    Run `Apex.UI.exe` as Administrator (optional, but recommended for first run to create ProgramData folders).
    -   Database created at: `C:\ProgramData\ApexPrintingSystem\Database\`
    -   Logs created at: `[AppDir]\Logs\`

## Troubleshooting

-   **Database Locked**: Ensure no other instance is running.
-   **Printer Not Found**: Verify Windows Printer Spooler service is running.
-   **UI Freeze**: Check `Logs/` for "UI Thread Error".

## Updates
-   Replace all files *except* `appsettings.json` (if any).
-   Database is safe in `ProgramData` and won't be overwritten.
