namespace MacroDeckHost.Integrations.Clipboard.Native;

[Flags]
internal enum ClipboardContentKinds
{
	None = 0,
	Text = 1,
	Image = 2,
	Files = 4,
	Other = 8
}
