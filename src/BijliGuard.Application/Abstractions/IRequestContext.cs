namespace BijliGuard.Application.Abstractions;

/// <summary>Exposes the current request's correlation ID to use cases without an HTTP dependency.</summary>
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
