using System.Text.Json;

namespace MacroDeck.Plugin.Protocol.Capabilities.Ui;

/// <summary>One surface a UI provider can serve. <see cref="Kind" /> and <see cref="SessionMode" /> are
/// strings because both vocabularies are deliberately open in <c>MacroDeck.Ui.Model</c> - an
/// unrecognised value round-trips rather than failing the declaration.</summary>
public sealed record UiSurfaceDescriptorDto
{
	public required string Kind { get; init; }

	public required string SessionMode { get; init; }
}

/// <summary>One named developer preview scenario a plugin declares. Developer-tooling data: a preview is
/// never reachable from a production surface, and a plugin that declares none is unaffected.</summary>
public sealed record UiPreviewDescriptorDto
{
	/// <summary>Addresses the scenario when a developer preview session is opened.</summary>
	public required string Id { get; init; }

	/// <summary>The view the scenario previews - scenarios that share it are listed together.</summary>
	public required string View { get; init; }

	/// <summary>The state the scenario shows, unique within <see cref="View" />.</summary>
	public required string Scenario { get; init; }

	/// <summary>Which primitive vocabulary the scenario's tree is authored in. A free string, like every
	/// other UI vocabulary on the wire.</summary>
	public required string Profile { get; init; }
}

/// <summary>The full result of the <c>ui</c> capability's <c>describe</c> operation - the surfaces this
/// provider can serve and the UI model version it speaks.</summary>
public sealed record UiDescribePayload
{
	public required IReadOnlyList<UiSurfaceDescriptorDto> Surfaces { get; init; }

	public required int UiModelVersion { get; init; }

	/// <summary>The developer preview scenarios this plugin declares, if any.</summary>
	/// <remarks>Optional rather than required: a plugin built against an SDK that predates previews omits
	/// the key entirely, and it must still describe successfully.</remarks>
	public IReadOnlyList<UiPreviewDescriptorDto>? Previews { get; init; }
}

/// <summary>Arguments for <c>session.open</c>. The session id is host-issued; a provider never mints
/// one.</summary>
public sealed record UiSessionOpenArguments
{
	public required string SessionId { get; init; }

	public required string SurfaceKind { get; init; }

	public required string SessionMode { get; init; }

	public JsonElement? SurfaceAttributes { get; init; }

	public required int UiModelVersion { get; init; }
}

/// <summary>Result of <c>session.open</c>. A provider that cannot serve the requested surface declines
/// here rather than opening a session it will immediately fault.</summary>
public sealed record UiSessionOpenResult
{
	public required bool Accepted { get; init; }

	public int? NegotiatedUiModelVersion { get; init; }

	public string? RejectionReason { get; init; }
}

/// <summary>Arguments for <c>session.close</c>.</summary>
public sealed record UiSessionCloseArguments
{
	public required string SessionId { get; init; }

	public string? Reason { get; init; }
}

/// <summary>Arguments for <c>session.snapshot</c>: a request for the session's current full tree. The
/// tree is not returned here - it arrives as a separate <c>host.invoke ui/snapshot</c>, so that one
/// delivery path serves both a first attach and a resync.</summary>
public sealed record UiSessionSnapshotArguments
{
	public required string SessionId { get; init; }
}

/// <summary>Arguments for <c>session.event</c>: the envelope <c>UiEvent</c>'s own contract defers to
/// the plugin protocol. <see cref="ClientId" /> identifies which attached client acted, and is
/// meaningful only for a shared session.</summary>
public sealed record UiSessionEventArguments
{
	public required string SessionId { get; init; }

	public required string NodeId { get; init; }

	public required string Name { get; init; }

	public JsonElement? Data { get; init; }

	public int? Revision { get; init; }

	public string? ClientId { get; init; }
}

/// <summary>
/// Arguments for <c>modal.result</c>: the outcome of a modal the plugin opened through
/// <c>HostOperations.ActionInteractions.ShowModal</c>, delivered whenever the user answers rather than
/// inside the opening call's deadline.
/// </summary>
public sealed record UiModalResultArguments
{
	/// <summary>The modal this outcome belongs to - the id the opening call returned.</summary>
	public required string ModalId { get; init; }

	/// <summary>
	/// Whether the modal ended without the user completing it. Everything that is not an explicit
	/// completion is a cancellation: dismissal, the client disconnecting, the flow being cancelled, the
	/// session faulting, the run reaching its limit. Exactly one <c>modal.result</c> is ever sent for a
	/// modal, so a waiting plugin always settles.
	/// </summary>
	public required bool Cancelled { get; init; }

	/// <summary>What the completion carried. Absent when <see cref="Cancelled" />.</summary>
	public JsonElement? Value { get; init; }
}
