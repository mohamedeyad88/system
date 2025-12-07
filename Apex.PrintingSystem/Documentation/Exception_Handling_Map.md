# Exception Handling Map

| Exception Type | Handler | Action | Log Level |
|---|---|---|---|
| **UI Exception** | `App.DispatcherUnhandledException` | Show `ErrorDialog` | Critical |
| **Background Thread** | `AppDomain.UnhandledException` | Log & Crash (Safe) | Critical |
| **Task Exception** | `TaskScheduler.UnobservedTaskException` | Log & Ignore | Critical |
| **Database Error** | `Repository<T>` | Log & Rethrow | Error |
| **Printer Error** | `PrinterService` | Log & Return Status | Warning |
| **Missing File** | `FileIngestService` | Log & Skip | Warning |

## Recovery Strategies

1.  **Database Connection Failed**:
    -   `DatabaseHealthService` attempts to repair.
    -   If fails, restores from Backup.
    
2.  **Corrupt Config**:
    -   `SettingsService` falls back to defaults.

3.  **Printer Unavailable**:
    -   `LoadBalancer` reroutes job to next available printer.
