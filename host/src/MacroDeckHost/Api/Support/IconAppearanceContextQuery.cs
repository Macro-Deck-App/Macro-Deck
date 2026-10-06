using MacroDeckHost.Application.Icons;

namespace MacroDeckHost.Api.Support;

public static class IconAppearanceContextQuery
{
	public static IconAppearanceContext FromRequest(HttpContext? context)
		=> context is null ? IconAppearanceContext.None : FromQuery(context.Request.Query);

	public static IconAppearanceContext FromQuery(IQueryCollection query)
		=> new(Value(query, IconAppearanceTraits.ColorScheme), Value(query, IconAppearanceTraits.Motion));

	private static string? Value(IQueryCollection query, string trait)
		=> query.TryGetValue(trait, out var values) &&
			values.Count == 1 &&
			values[0] is { Length: > 0 and <= 32 } value
				? value
				: null;
}
