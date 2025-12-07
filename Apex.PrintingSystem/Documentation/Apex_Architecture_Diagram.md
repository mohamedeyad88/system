# Apex Printing System - Architecture

```mermaid
graph TD
    User[User] --> UI[Apex.UI (WPF)]
    
    subgraph "Presentation Layer"
        UI --> MainVM[MainViewModel]
        UI --> PrintMgrVM[PrintManagerViewModel]
        UI --> NumBooksVM[NumberedBooksViewModel]
        UI --> PerfVM[SystemPerformanceViewModel]
    end

    subgraph "Service Layer (Apex.Services)"
        MainVM --> Logger[FileLoggerService]
        MainVM --> Settings[SettingsService]
        PrintMgrVM --> PrintJobMgr[PrintJobManager]
        PrintJobMgr --> Router[RoutingRulesEngine]
        PrintJobMgr --> Balancer[LoadBalancer]
        NumBooksVM --> NumEngine[NumberedBooksEngine]
    end

    subgraph "Core Layer (Apex.Core)"
        Logger -.-> ILogger[ILoggerService]
        Settings -.-> ISettings[ISettingsService]
        PrintJobMgr -.-> IPrintMgr[IPrintJobManager]
    end

    subgraph "Data Layer (Apex.Data)"
        Settings --> Repo[Repository<T>]
        Repo --> Db[ApexDbContext]
        Db --> SQLite[(SQLite Database)]
        DbInit[DbInitializer] --> Db
        DbInit --> Health[DatabaseHealthService]
    end

    subgraph "External Systems"
        PrintJobMgr --> WindowsSpooler[Windows Print Spooler]
        NumEngine --> PDF[PdfSharpCore]
        NumEngine --> Skia[SkiaSharp]
    end
```
