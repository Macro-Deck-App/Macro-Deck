using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Integrations.Clipboard.State;

internal sealed record ClipboardSnapshot(ClipboardContentKinds Kinds, string? Text, bool TextTooLarge)
{
	public string ContentType => ClipboardContentType.Of(Kinds);

	public bool HasText => Kinds.HasFlag(ClipboardContentKinds.Text);

	public bool HasImage => Kinds.HasFlag(ClipboardContentKinds.Image);

	public bool HasFiles => Kinds.HasFlag(ClipboardContentKinds.Files);
}
