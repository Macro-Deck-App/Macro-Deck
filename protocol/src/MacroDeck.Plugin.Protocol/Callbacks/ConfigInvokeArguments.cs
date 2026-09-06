namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>Arguments for <c>host.invoke</c> against <see cref="HostApis.Config"/>'s <c>get-string</c>
/// and <c>get-secret</c> operations. <c>entries</c> needs no arguments.</summary>
public sealed record ConfigGetArguments
{
	public required Guid EntryId { get; init; }

	public required string Key { get; init; }
}

public sealed record ConfigSetStringArguments
{
	public required Guid EntryId { get; init; }

	public required string Key { get; init; }

	public string? Value { get; init; }
}

public sealed record ConfigSetSecretArguments
{
	public required Guid EntryId { get; init; }

	public required string Key { get; init; }

	public required string Value { get; init; }
}
