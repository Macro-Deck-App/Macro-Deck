using MacroDeckHost.Rendering.Dom;

namespace MacroDeckHost.Rendering;

public sealed record RenderTheme
{
	private RenderTheme(bool light, string? accent)
	{
		IsLight = light;
		Accent = accent;
	}

	public static RenderTheme Dark { get; } = new(false, null);

	public static RenderTheme Light { get; } = new(true, null);

	public bool IsLight { get; }

	public string? Accent { get; }

	public RenderTheme WithAccent(string hex)
	{
		return new RenderTheme(IsLight, hex);
	}

	internal IReadOnlyDictionary<string, string> Tokens()
	{
		var sheet = StyleSheet.Shared;
		var tokens = new Dictionary<string, string>(sheet.DarkTokens, StringComparer.Ordinal);
		if (IsLight)
		{
			foreach (var (name, value) in sheet.LightTokens)
			{
				tokens[name] = value;
			}
		}

		if (Accent is { } accent)
		{
			tokens["--color-accent"] = accent;
		}

		return tokens;
	}
}
