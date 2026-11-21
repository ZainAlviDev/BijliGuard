using BijliGuard.Application.Abstractions;

namespace BijliGuard.Infrastructure.Time;

public sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}
