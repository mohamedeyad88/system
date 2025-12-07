# ILoggerService API Documentation

## Interface
`Apex.Core.Interfaces.ILoggerService`

## Methods

### `Log`
Logs a message with a specific severity level.

```csharp
void Log(LogLevel level, string message, string? component = null, string? method = null, Exception? ex = null, object? data = null);
```

-   **level**: `Info`, `Warning`, `Error`, `Critical`
-   **message**: Human-readable description.
-   **component**: Class or Module name (optional, auto-detected).
-   **method**: Method name (optional, auto-detected).
-   **ex**: Exception object (optional).
-   **data**: Anonymous object for structured logging (optional).

### `LogPerformance`
Logs performance metrics.

```csharp
void LogPerformance(string operation, TimeSpan duration, long memoryUsedBytes);
```

### `LogPrintJob`
Logs print job lifecycle events.

```csharp
void LogPrintJob(string printerName, string jobName, string status, string details);
```

### `CleanOldLogsAsync`
Deletes logs older than specified days.

```csharp
Task CleanOldLogsAsync(int daysToKeep = 30);
```

### `GetTodayLogPath`
Returns the absolute path to the current day's log file.

```csharp
string GetTodayLogPath();
```
