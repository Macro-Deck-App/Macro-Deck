namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreDependencyList
{
	public string? Ecosystem { get; init; }

	public string? SdkVersion { get; init; }

	public required IReadOnlyList<StoreDependencyFramework> Frameworks { get; init; }
}
