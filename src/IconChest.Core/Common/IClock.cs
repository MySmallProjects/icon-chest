namespace IconChest.Core.Common;

/// <summary>
/// Abstraction for the system clock to allow deterministic testing of time-dependent features.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime TodayUtc => UtcNow.Date;
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;
}

