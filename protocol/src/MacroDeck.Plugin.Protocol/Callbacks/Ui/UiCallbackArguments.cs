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

/// <summary>Arguments for <c>host.invoke ui/register-resource</c>. <see cref="ContentHash" /> names bytes
/// this plugin uploaded as kind <c>ui-resource</c>; <see cref="MediaType" /> must match the type that
/// upload declared.</summary>
public sealed record UiRegisterResourceArguments
{
	public required string Name { get; init; }

	public required string ContentHash { get; init; }

	public required string MediaType { get; init; }
}

/// <summary>Result of <c>host.invoke ui/register-resource</c>. Either <see cref="Resource" /> is present or
/// <see cref="UploadRequired" /> is <c>true</c>: when the host does not hold the named bytes, the plugin
/// uploads them and registers again.</summary>
public sealed record UiRegisterResourceResult
{
	public UiResourceHandleDto? Resource { get; init; }

	public bool UploadRequired { get; init; }
}

/// <summary>Wire copy of the handle a tree references. <see cref="ResourceId" /> is opaque and stays the
/// same when the name is registered again; <see cref="ContentHash" /> changes with the bytes.</summary>
public sealed record UiResourceHandleDto
{
	public required string ResourceId { get; init; }

	public required string ContentHash { get; init; }

	public required string MediaType { get; init; }

	public required int ByteLength { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/remove-resource</c>.</summary>
public sealed record UiRemoveResourceArguments
{
	public required string Name { get; init; }
}
