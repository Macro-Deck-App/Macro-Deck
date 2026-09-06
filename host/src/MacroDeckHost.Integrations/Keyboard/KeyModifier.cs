namespace MacroDeckHost.Integrations.Keyboard;

[Flags]
public enum KeyModifier
{
	None = 0,
	Control = 1 << 0,
	Shift = 1 << 1,
	Alt = 1 << 2,
	Meta = 1 << 3
}
