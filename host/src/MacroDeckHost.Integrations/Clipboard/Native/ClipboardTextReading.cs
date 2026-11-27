namespace MacroDeckHost.Integrations.Clipboard.Native;

internal readonly record struct ClipboardTextReading(string? Text, bool IsTooLarge)
{
	public static ClipboardTextReading None => new(null, false);

	public static ClipboardTextReading TooLarge => new(null, true);

	public bool HasText => Text is not null;

	public static ClipboardTextReading Of(string text)
	{
		return new ClipboardTextReading(text, false);
	}
}
