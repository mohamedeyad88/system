# Data Flow Document

## 1. Print Job Lifecycle

1.  **Ingestion**:
    -   User selects file -> `PrintManagerViewModel`
    -   `FileIngestService` validates and copies file to `Temp/Ingest`.
    
2.  **Processing**:
    -   `PrintJobManager` creates `PrintJob` entity.
    -   `RoutingRulesEngine` determines target printer.
    -   Job is saved to SQLite via `IRepository<PrintJob>`.

3.  **Execution**:
    -   `PrintJobProcessor` (ThreadPool) picks up job from `Channel<PrintJob>`.
    -   Sends to `IPrinterService` (Windows Spooler).
    -   Updates status to `Printing` -> `Completed`.

4.  **Logging**:
    -   `FileLoggerService` records every state change.
    -   `PrinterMonitoringService` updates Printer Status cache.

## 2. Numbered Books Generation

1.  **Configuration**:
    -   User sets Start/End numbers in `NumberedBooksViewModel`.
    
2.  **Generation**:
    -   `NumberedBooksEngine` generates PDF pages in memory (SkiaSharp).
    -   PDF is saved to `Output/Books`.

3.  **Printing**:
    -   Generated PDF is fed back into **Print Job Lifecycle** as a new job.

## 3. System Health & Logs

1.  **Monitoring**:
    -   `PrinterMonitoringService` polls printers every 5s.
    -   Updates `ICacheService`.
    
2.  **Diagnostics**:
    -   `PrinterDiagnosticsViewModel` reads from `ICacheService`.
    
3.  **Maintenance**:
    -   `LogMaintenanceService` deletes old logs on startup.
    -   `DatabaseHealthService` backs up DB weekly.
