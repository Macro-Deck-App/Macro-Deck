namespace MacroDeck.Plugin.Protocol.Callbacks.IconPacks;

/// <summary>Arguments for <c>host.invoke icon-packs/sync-bundled</c>: the complete set of bundled packs the
/// development session declares. A key the host holds for this plugin but that is missing here is removed.</summary>
public sealed record IconPackSyncArguments
{
	public required IReadOnlyList<BundledIconPackDeclarationDto> Packs { get; init; }
}

/// <summary>One declared pack: its key and the identity of the archive uploaded as kind <c>icon-pack</c>.</summary>
public sealed record BundledIconPackDeclarationDto
{
	public required string Key { get; init; }

	public required string ContentHash { get; init; }

	public required int ByteLength { get; init; }
}

/// <summary>Result of <c>host.invoke icon-packs/sync-bundled</c>. When <see cref="UploadRequired" /> is not
/// empty nothing was synced: the plugin uploads those archives and syncs again. Otherwise
/// <see cref="Changed" /> says whether any pack was added, replaced or removed.</summary>
public sealed record IconPackSyncResult
{
	public IReadOnlyList<string> UploadRequired { get; init; } = [];

	public bool Changed { get; init; }
}

/// <summary>Arguments for <c>host.invoke icon-packs/get-icon-resource</c>. Answered with a
/// <see cref="Ui.UiResourceHandleDto" />.</summary>
public sealed record GetIconResourceArguments
{
	public required string Key { get; init; }

	public required string Name { get; init; }
}
