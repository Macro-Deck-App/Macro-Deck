namespace MacroDeckHost.Application.Plugins.Capabilities;

public sealed record CapabilityInvokeRequest
{
	public required string Kind { get; init; }

	public required string LocalId { get; init; }

	public required string Operation { get; init; }

	public object? Arguments { get; init; }

	public TimeSpan? Timeout { get; init; }

	public string? IdempotencyKey { get; init; }
}
