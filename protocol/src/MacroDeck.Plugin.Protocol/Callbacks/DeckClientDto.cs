namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>One client's position in a <see cref="DeckStateDto" /> push.</summary>
public sealed record DeckClientDto
{
	public string ClientId { get; init; } = string.Empty;

	public string? DeviceId { get; init; }

	public string ProfileId { get; init; } = string.Empty;

	public string FolderId { get; init; } = string.Empty;
}
