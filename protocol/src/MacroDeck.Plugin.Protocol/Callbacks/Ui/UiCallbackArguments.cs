using System.Text.Json;
using MacroDeck.Localization;

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

/// <summary>Arguments for <c>host.invoke ui/reload</c>: the provider's code changed under .NET Hot Reload
/// and this session has to be built again. The host ends the session and every attached client opens it
/// again, which asks the provider for a new session.</summary>
public sealed record UiReloadArguments
{
	public required string SessionId { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/report-parameter-values</c>: values for the declared parameters
/// of the action <see cref="SessionId" /> configures, keyed by parameter name, in the JSON shape Macro Deck
/// stores for each parameter type.</summary>
public sealed record UiReportParameterValuesArguments
{
	public required string SessionId { get; init; }

	public required IReadOnlyDictionary<string, JsonElement> Values { get; init; }
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

/// <summary>Arguments for <c>host.invoke ui/register-music-player-artwork</c>. <see cref="InstanceId" /> is the
/// qualified id, <c>integrationId::instanceId</c>, that a music player's album-art URL carries.</summary>
public sealed record UiRegisterMusicPlayerArtworkArguments
{
	public required string Name { get; init; }

	public required string InstanceId { get; init; }

	public required string ArtworkId { get; init; }

	/// <summary>Optional. The size in pixels the cover is shown at: the host registers the smallest copy it
	/// keeps that is at least this large on its longer side, or the full-size cover when it keeps none.
	/// Absent, the full-size cover. A host that predates the field ignores it and registers the full-size
	/// cover.</summary>
	public int? MaxSize { get; init; }
}

/// <summary>Result of <c>host.invoke ui/register-music-player-artwork</c>. <see cref="Resource" /> is absent when
/// no such music player or artwork exists.</summary>
public sealed record UiRegisterMusicPlayerArtworkResult
{
	public UiResourceHandleDto? Resource { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/remove-resource</c>.</summary>
public sealed record UiRemoveResourceArguments
{
	public required string Name { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/origin.report-failure</c>: a provider reporting that what the
/// client asked for through an event did not work. The host shows <see cref="Message" /> on that client the
/// way it shows a failed action, and only while the client is attached to the session, which must be one of
/// the calling plugin's.</summary>
public sealed record UiOriginReportFailureArguments
{
	public required string SessionId { get; init; }

	/// <summary>The client that raised the event, as <c>ui/session.event</c> carried it.</summary>
	public required string ClientId { get; init; }

	/// <summary>The name of the event that failed, for the client's own feedback. Optional.</summary>
	public string? EventName { get; init; }

	public required LocalizedText Message { get; init; }
}

/// <summary>Result of <c>host.invoke ui/origin.report-failure</c>. <see cref="Delivered" /> is false when
/// the client is no longer attached to the session.</summary>
public sealed record UiOriginReportFailureResult
{
	public required bool Delivered { get; init; }
}

/// <summary>Arguments for <c>host.invoke ui/origin.show-modal</c>: opens one of the calling plugin's dialogs
/// on the client that raised an event in one of its sessions. Opened only while that client is attached to
/// the session. The answer arrives as a <c>ui</c>/<c>modal.result</c> capability invoke when
/// <see cref="AwaitResult" /> is set, exactly as for <c>action-interactions/show-modal</c>.</summary>
public sealed record UiOriginShowModalArguments
{
	public required string SessionId { get; init; }

	public required string ClientId { get; init; }

	public required string ViewId { get; init; }

	public LocalizedText? Title { get; init; }

	public IReadOnlyDictionary<string, JsonElement>? Data { get; init; }

	public bool AwaitResult { get; init; }
}

/// <summary>Result of <c>host.invoke ui/origin.show-modal</c>.</summary>
public sealed record UiOriginShowModalResult
{
	public required bool Opened { get; init; }

	/// <summary>The id a later <c>modal.result</c> names. Empty when <see cref="Opened" /> is false.</summary>
	public string ModalId { get; init; } = string.Empty;
}

/// <summary>Arguments for <c>host.invoke ui/origin.open-url</c>: opens <see cref="Url" /> in the browser of the
/// client that raised an event in one of the calling plugin's sessions. <see cref="Url" /> must be an absolute
/// <c>http</c> or <c>https</c> URL.</summary>
public sealed record UiOriginOpenUrlArguments
{
	public required string SessionId { get; init; }

	/// <summary>The client that raised the event, as <c>ui/session.event</c> carried it.</summary>
	public required string ClientId { get; init; }

	public required string Url { get; init; }
}

/// <summary>Result of <c>host.invoke ui/origin.open-url</c>. <see cref="Delivered" /> is <c>true</c> when the
/// URL was handed to the client, which does not prove a browser opened it.</summary>
public sealed record UiOriginOpenUrlResult
{
	public required bool Delivered { get; init; }
}
