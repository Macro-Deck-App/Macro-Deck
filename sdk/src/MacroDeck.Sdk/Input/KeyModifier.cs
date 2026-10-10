namespace MacroDeck.Sdk.Input;

/// <summary>The modifier keys of a key combination. The <c>Right</c> members mean the right-hand key specifically.</summary>
[Flags]
public enum KeyModifier
{
	None = 0,
	Control = 1 << 0,
	Shift = 1 << 1,
	Alt = 1 << 2,
	Meta = 1 << 3,
	RightControl = 1 << 4,
	RightShift = 1 << 5,
	RightAlt = 1 << 6,
	RightMeta = 1 << 7
}
