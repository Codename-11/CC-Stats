namespace CCStats.Core.Models;

public record ResetEvent(
    DateTimeOffset Timestamp,
    string WindowType,
    double UtilizationBefore,
    double UtilizationAfter);
