namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreExtensionLink
{
	public required string Type { get; init; }

	public required string Url { get; init; }

	public string? Label { get; init; }
}
