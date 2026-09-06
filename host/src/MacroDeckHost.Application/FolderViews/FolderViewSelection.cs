using System.Text.Json;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.FolderViews;

/// <summary>What a folder stores about how it is rendered.</summary>
/// <param name="ViewId">Null for the built-in widget grid.</param>
/// <param name="Configuration">JSON object text, or null when the view has none.</param>
public readonly record struct FolderViewSelection(string? ViewId, string? Configuration)
{
	/// <summary>
	/// Validates a requested view and configuration against the live catalog.
	/// </summary>
	/// <remarks>
	/// Deliberately not applied on the import path. A folder arriving in an archive may name a view
	/// nothing provides yet - the whole point of retaining an unresolvable id is that reinstalling the
	/// integration restores the folder - whereas a user picking a view in the editor is choosing from a
	/// list, so an id that resolves to nothing there is a bug, not a folder to accept and break.
	/// </remarks>
	public static Result<FolderViewSelection, FolderError> Validate(
		IFolderViewRegistry registry,
		string? viewId,
		string? configuration)
	{
		ArgumentNullException.ThrowIfNull(registry);

		if (BuiltInFolderViews.IsWidgetGrid(viewId))
		{
			// The grid configures nothing, so carrying a configuration for it would be state no reader
			// ever reads and every writer would have to keep consistent.
			return Result.Ok<FolderViewSelection, FolderError>(new FolderViewSelection(null, null));
		}

		if (!registry.TryResolve(viewId!, out _))
		{
			return Result.Fail<FolderViewSelection, FolderError>(FolderError.UnknownFolderView,
				$"No folder view '{viewId}' is available.");
		}

		if (configuration is null)
		{
			return Result.Ok<FolderViewSelection, FolderError>(new FolderViewSelection(viewId, null));
		}

		if (!IsJsonObject(configuration))
		{
			return Result.Fail<FolderViewSelection, FolderError>(FolderError.InvalidFolderViewConfiguration,
				"A folder view configuration must be a JSON object.");
		}

		return Result.Ok<FolderViewSelection, FolderError>(new FolderViewSelection(viewId, configuration));
	}

	private static bool IsJsonObject(string candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(candidate);
			return document.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
