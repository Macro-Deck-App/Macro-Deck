using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroDeckHost.Infrastructure.Migration.MacroDeck2;

/// <summary>
/// The subset of Macro Deck 2's serialized shapes this migration reads. Property names are PascalCase as
/// Json.NET wrote them; the reader is configured case-insensitively, so a file written by an older build
/// with different casing still loads.
/// </summary>
internal sealed class MacroDeck2Profile
{
	public string ProfileId { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	public int Rows { get; set; }

	public int Columns { get; set; }

	public int ButtonSpacing { get; set; }

	public int ButtonRadius { get; set; }

	public bool ButtonBackground { get; set; } = true;

	public List<MacroDeck2Folder> Folders { get; set; } = [];
}

internal sealed class MacroDeck2Folder
{
	public string FolderId { get; set; } = string.Empty;

	public string DisplayName { get; set; } = string.Empty;

	/// <summary>Ids of this folder's children. Macro Deck 3 stores the opposite direction.</summary>
	public List<string> Childs { get; set; } = [];

	public List<MacroDeck2Button> ActionButtons { get; set; } = [];

	public List<string> ApplicationsFocusDevices { get; set; } = [];

	public string? ApplicationToTrigger { get; set; }

	/// <summary>Macro Deck 2 computes this from the name rather than storing it; older files stored it.</summary>
	public bool IsRoot => string.Equals(DisplayName, "*Root*", StringComparison.Ordinal);
}

internal sealed class MacroDeck2Button
{
	public string Guid { get; set; } = string.Empty;

	public bool State { get; set; }

	public string? IconOff { get; set; }

	public string? IconOn { get; set; }

	public string? BackColorOff { get; set; }

	public string? BackColorOn { get; set; }

	public MacroDeck2Label? LabelOff { get; set; }

	public MacroDeck2Label? LabelOn { get; set; }

	[JsonPropertyName("Position_X")]
	public int PositionX { get; set; }

	[JsonPropertyName("Position_Y")]
	public int PositionY { get; set; }

	public string? StateBindingVariable { get; set; }

	public List<MacroDeck2Action> Actions { get; set; } = [];

	public List<MacroDeck2Action> ActionsRelease { get; set; } = [];

	public List<MacroDeck2Action> ActionsLongPress { get; set; } = [];

	public List<MacroDeck2Action> ActionsLongPressRelease { get; set; } = [];

	public int ModifierKeyCodes { get; set; }

	public int KeyCode { get; set; }
}

internal sealed class MacroDeck2Label
{
	public string LabelText { get; set; } = string.Empty;

	/// <summary>0 = TOP, 1 = CENTER, 2 = BOTTOM.</summary>
	public int LabelPosition { get; set; } = 2;

	public string? LabelColor { get; set; }

	public float Size { get; set; } = 6;

	public string? FontFamily { get; set; }

	// LabelBase64 is deliberately absent: it is a pre-rendered PNG of the text above, which Macro Deck 3
	// draws itself. Reading it would only carry a stale bitmap of the old font across.
}

internal sealed class MacroDeck2Action
{
	[JsonPropertyName("$type")]
	public string? Type { get; set; }

	public string? Name { get; set; }

	public string? Description { get; set; }

	/// <summary>Opaque: each plugin chose its own format, and most - but not all - wrote JSON.</summary>
	public string? Configuration { get; set; }

	public string? ConfigurationSummary { get; set; }

	/// <summary>
	/// Splits Json.NET's assembly-qualified name into its type and assembly halves. No assembly is ever
	/// loaded - the string is the only thing that identifies the foreign plugin here.
	/// </summary>
	public (string TypeName, string Assembly) SplitType()
	{
		if (string.IsNullOrWhiteSpace(Type))
		{
			return (string.Empty, string.Empty);
		}

		var separator = Type.IndexOf(',', StringComparison.Ordinal);
		return separator < 0
			? (Type.Trim(), string.Empty)
			: (Type[..separator].Trim(), Type[(separator + 1)..].Trim());
	}
}

internal static class MacroDeck2Json
{
	public static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};
}
