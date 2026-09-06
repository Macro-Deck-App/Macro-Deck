using System.Text.Json;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

/// <summary>Opens a UI session for a config entry point - see <c>UiConfigEntryPoints</c> for
/// <see cref="EntryPoint" />'s well-known values.</summary>
public sealed record OpenConfigUiSessionRequest
{
	public string EntryPoint { get; init; } = string.Empty;

	public string IntegrationId { get; init; } = string.Empty;

	/// <summary>The flow id <c>StartConfigFlow</c> returned. Only on <c>integration-config</c>.</summary>
	public string? FlowId { get; init; }

	/// <summary>Only on <c>action-config</c>.</summary>
	public string? ActionId { get; init; }

	/// <summary>The values already stored for the action instance being configured. Only on
	/// <c>action-config</c>.</summary>
	public Dictionary<string, JsonElement>? Parameters { get; init; }

	/// <summary>The folder whose view is being configured. Only on <c>folder-view-config</c>.</summary>
	public string? FolderId { get; init; }

	/// <summary>The folder view being configured. Only on <c>folder-view-config</c>. Not read from the
	/// folder: the picker configures a view before it is saved, so the view being configured is often not
	/// the one the folder currently stores.</summary>
	public string? FolderViewId { get; init; }

	/// <summary>The configuration to seed the surface with, as JSON object text. Only on
	/// <c>folder-view-config</c>; absent starts from the folder's stored configuration.</summary>
	public string? FolderViewConfiguration { get; init; }

	/// <summary>The widget being configured. Only on <c>widget-config</c>.</summary>
	public string? WidgetId { get; init; }

	/// <summary>The configuration to seed the surface with, as JSON object text. Only on
	/// <c>widget-config</c>; absent starts from the widget's stored configuration. The editor owns the
	/// draft and may be holding one the widget has not been saved with yet - a JSON-mode edit, above all -
	/// so a session opened without this shows the tree the stored data describes, not the one being edited.
	/// </summary>
	public string? WidgetData { get; init; }
}

public sealed record OpenConfigUiSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}

public sealed record UiAttachSessionRequest
{
	public string SessionId { get; init; } = string.Empty;
}

public sealed record UiAttachSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }

	public string SurfaceKind { get; init; } = string.Empty;

	// The mode the session will actually behave as, not the one that was declared: an
	// unrecognised declared mode is reported here as exclusive, which is how it is enforced.
	public string SessionMode { get; init; } = string.Empty;

	public int Revision { get; init; }
}

public sealed record OpenWidgetUiSessionRequest
{
	public string? WidgetId { get; init; }

	public string? WidgetType { get; init; }

	public JsonElement? Data { get; init; }

	public bool Ghost { get; init; }

	/// <summary>The stored widget a preview's draft belongs to, so its widget-scoped variables resolve -
	/// see <see cref="MacroDeck.Ui.Model.Surfaces.UiWidgetSurfaceAttributes.VariableScopeWidgetId" />. A
	/// value that is not a Guid, or that names no stored widget, is ignored rather than refused: a widget
	/// that has never been saved has no id to send yet. Ignored on a live widget, which already resolves
	/// against itself.</summary>
	public string? VariableScopeWidgetId { get; init; }

	/// <summary>Asks a preview for a representative sample of the widget rather than its live state -
	/// see <see cref="MacroDeck.Ui.Model.Surfaces.UiWidgetSurfaceAttributes.Sample" />. Ignored on a
	/// live widget, which has state of its own to show.</summary>
	public bool Sample { get; init; }
}

public sealed record OpenWidgetUiSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}

/// <summary>Opens a UI session for a folder rendered by a folder view provider. The built-in widget grid
/// is never opened this way - the client renders it itself.</summary>
public sealed record OpenFolderUiSessionRequest
{
	public string FolderId { get; init; } = string.Empty;
}

public sealed record OpenFolderUiSessionResponse
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }

	/// <summary>The view the folder selected, echoed so a client rendering the placeholder can name it
	/// even when the session was rejected.</summary>
	public string ViewId { get; init; } = string.Empty;

	/// <summary>Whether Macro Deck should draw its own back button - see the SDK's
	/// <c>FolderViewNavigation</c>. Absent when the view could not be resolved, in which case the client
	/// shows its own navigation around the placeholder.</summary>
	public string? Navigation { get; init; }
}

public sealed record UiSendEventRequest
{
	public string SessionId { get; init; } = string.Empty;

	public string NodeId { get; init; } = string.Empty;

	public string Name { get; init; } = string.Empty;

	public UiRawJson Data { get; init; }

	public int? Revision { get; init; }
}

public sealed record UiSendEventResponse
{
	public required bool Accepted { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }
}

public sealed record UiSessionTreeUpdatedEvent
{
	public required string SessionId { get; init; }

	public required int Revision { get; init; }

	public required UiRawJson Tree { get; init; }
}

public sealed record UiSessionPatchedEvent
{
	public required string SessionId { get; init; }

	public required int FromRevision { get; init; }

	public required int ToRevision { get; init; }

	public required UiRawJson Patch { get; init; }
}

public sealed record UiSessionInvalidatedEvent
{
	public required string SessionId { get; init; }

	public required string Code { get; init; }

	public required string Message { get; init; }

	public required bool Retryable { get; init; }
}

public sealed record UiSessionClosedEvent
{
	public required string SessionId { get; init; }

	public string? Reason { get; init; }
}
