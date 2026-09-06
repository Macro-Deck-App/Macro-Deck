namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreMediaAsset
{
	public required Uri Url { get; init; }

	public required string Sha256 { get; init; }

	public required long Size { get; init; }

	public string? ContentType { get; init; }

	public string? Caption { get; init; }
}
