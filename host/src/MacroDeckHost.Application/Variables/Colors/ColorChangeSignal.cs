namespace MacroDeckHost.Application.Variables.Colors;

// Raised whenever something a colour value can resolve through may have changed: a Color variable's
// value, availability or name, a variable that shadows one, or a widget going away.
public sealed class ColorChangeSignal
{
	public event Action? Changed;

	public void Raise() => Changed?.Invoke();
}
