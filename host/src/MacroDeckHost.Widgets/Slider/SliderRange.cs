using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Widgets.Slider;

internal static class SliderRange
{
	// A provider's declared bound is an unvalidated double; a non-finite one falls back like an absent one,
	// since UiCanonicalJson's strict number handling would fault the session writing NaN.
	public static (double Min, double Max) Resolve(VariableEntity? variable, double fallbackMin, double fallbackMax)
		=> (Bound(variable?.Min, fallbackMin), Bound(variable?.Max, fallbackMax));

	public static double Bound(double? declared, double fallback)
		=> declared is { } value && double.IsFinite(value) ? value : fallback;
}
