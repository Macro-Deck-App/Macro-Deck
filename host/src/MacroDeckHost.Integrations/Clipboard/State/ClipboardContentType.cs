using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard.ContentTypes;

namespace MacroDeckHost.Integrations.Clipboard.State;

internal static class ClipboardContentType
{
	public const string Empty = "empty";
	public const string Text = "text";
	public const string Image = "image";
	public const string Files = "files";
	public const string Other = "other";

	public static IReadOnlyList<ActionParameterOption> Options { get; } =
	[
		new() { Value = Empty, Label = Strings.Empty() },
		new() { Value = Text, Label = Strings.Text() },
		new() { Value = Image, Label = Strings.Image() },
		new() { Value = Files, Label = Strings.Files() },
		new() { Value = Other, Label = Strings.Other() }
	];

	// Copied files usually carry their names as text and an icon as an image, so files rank first.
	public static string Of(ClipboardContentKinds kinds)
	{
		if (kinds.HasFlag(ClipboardContentKinds.Files))
		{
			return Files;
		}

		if (kinds.HasFlag(ClipboardContentKinds.Image))
		{
			return Image;
		}

		if (kinds.HasFlag(ClipboardContentKinds.Text))
		{
			return Text;
		}

		return kinds == ClipboardContentKinds.None ? Empty : Other;
	}
}
