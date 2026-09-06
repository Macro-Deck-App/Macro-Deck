using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Callbacks.Ui;

/// <summary>Arguments for <c>host.invoke ui/snapshot</c>. <see cref="Tree" /> stays an opaque
/// <see cref="JsonElement" /> on purpose: the host relays the provider's bytes to attached clients
/// without binding them to <c>UiTree</c>, so a patch never round-trips through the model.</summary>
public sealed record UiSnapshotArguments
{
	public required string SessionId { get; init; }

	public required JsonElement Tree { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/patch</c>. <see cref="Patch" /> is opaque for the same
/// reason as <c>UiSnapshotArguments.Tree</c>; the host reads the revisions out of it during validation
/// rather than duplicating them here, so there is one source of truth for the revision chain.</summary>
public sealed record UiPatchArguments
{
	public required string SessionId { get; init; }

	public required JsonElement Patch { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/fault</c>: the provider reporting that it can no longer
/// serve this session. The host turns it into a terminal, client-visible session error.</summary>
public sealed record UiFaultArguments
{
	public required string SessionId { get; init; }

	public required string Code { get; init; }

	public string? Message { get; init; }
}
