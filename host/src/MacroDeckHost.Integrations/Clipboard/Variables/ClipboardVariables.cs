using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Clipboard.Configuration;
using MacroDeckHost.Integrations.Clipboard.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard.Variables;

namespace MacroDeckHost.Integrations.Clipboard.Variables;

internal static class ClipboardVariables
{
	public const string ContentType = "clipboard_content_type";
	public const string HasText = "clipboard_has_text";
	public const string HasImage = "clipboard_has_image";
	public const string HasFiles = "clipboard_has_files";
	public const string Text = "clipboard_text";

	private static readonly IReadOnlyList<VariableDefinition> _state =
	[
		Declare(ContentType, VariableType.Text, Strings.ContentType()),
		Declare(HasText, VariableType.Boolean, Strings.HasText()),
		Declare(HasImage, VariableType.Boolean, Strings.HasImage()),
		Declare(HasFiles, VariableType.Boolean, Strings.HasFiles())
	];

	private static readonly VariableDefinition _text = Declare(Text, VariableType.Text, Strings.Text());

	public static IReadOnlyList<VariableDefinition> All { get; } = [.. _state, _text];

	public static IReadOnlyList<VariableDefinition> For(ClipboardSettings settings)
	{
		if (!settings.Watch)
		{
			return [];
		}

		return settings.ShowsText ? All : _state;
	}

	public static string? NameOf(string localId)
	{
		return All.FirstOrDefault(definition => string.Equals(definition.ResolvedId, localId, StringComparison.Ordinal))
			?.Name;
	}

	public static VariableReading Read(string? name, ClipboardSnapshot? snapshot, ClipboardSettings settings)
	{
		if (snapshot is null || name is null || !For(settings).Any(definition => definition.Name == name))
		{
			return VariableReading.Unavailable;
		}

		return name switch
		{
			ContentType => VariableReading.Of(snapshot.ContentType),
			HasText => VariableReading.Of(snapshot.HasText),
			HasImage => VariableReading.Of(snapshot.HasImage),
			HasFiles => VariableReading.Of(snapshot.HasFiles),
			Text when snapshot.TextTooLarge => VariableReading.Unavailable,
			Text => VariableReading.Of(snapshot.Text ?? string.Empty),
			_ => VariableReading.Unavailable
		};
	}

	private static VariableDefinition Declare(string name, VariableType type, LocalizedString label)
	{
		return VariableDefinition.Eager(name, type) with { DisplayName = label };
	}
}
