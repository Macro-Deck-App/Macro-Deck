namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Deck"/>'s <c>change-folder</c>
/// operation.</summary>
public sealed record DeckChangeFolderArguments
{
	public required string FolderId { get; init; }

	public string? OriginClientId { get; init; }
}

/// <summary>Arguments for <c>change-profile</c>.</summary>
public sealed record DeckChangeProfileArguments
{
	public required string ProfileId { get; init; }

	public string? OriginClientId { get; init; }
}

/// <summary>Arguments for <c>parent</c> and <c>back</c>, which take nothing but the origin client.</summary>
public sealed record DeckOriginArguments
{
	public string? OriginClientId { get; init; }
}
