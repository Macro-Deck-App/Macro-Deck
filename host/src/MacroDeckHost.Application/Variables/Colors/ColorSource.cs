using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables.Colors;

public static class ColorSource
{
	public static (string? Value, string? Source) Present(
		string? stored,
		IColorReferenceResolver? colors,
		VariableScope scope = VariableScope.Global,
		string? scopeRefId = null)
	{
		if (stored is null || !ColorReference.TryParse(stored, out _))
		{
			return (stored, null);
		}

		var resolved = colors?.Resolve(stored, scope, scopeRefId);
		return (string.IsNullOrEmpty(resolved) ? null : resolved, stored);
	}

	// Clients that predate references send back the resolved colour they were shown; that value keeps the
	// stored reference instead of freezing it. Null means leave the stored value alone.
	public static string? Incoming(
		string? source,
		string? value,
		string? stored,
		IColorReferenceResolver? colors,
		Func<string, string>? present = null)
	{
		if (source is not null)
		{
			return source;
		}

		if (value is null || stored is null || colors is null || !ColorReference.TryParse(stored, out _))
		{
			return value;
		}

		var resolved = colors.Resolve(stored);
		if (present is not null)
		{
			resolved = present(resolved);
		}

		return SameColor(value, resolved) ? null : value;
	}

	private static bool SameColor(string left, string right)
		=> string.Equals(RgbaColor.Canonicalize(left) ?? left.Trim(),
			RgbaColor.Canonicalize(right) ?? right.Trim(),
			StringComparison.OrdinalIgnoreCase);
}
