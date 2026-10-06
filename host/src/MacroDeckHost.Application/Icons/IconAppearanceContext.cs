namespace MacroDeckHost.Application.Icons;

public sealed record IconAppearanceContext(string? ColorScheme, string? Motion)
{
	public static readonly IconAppearanceContext None = new(null, null);

	public bool IsEmpty => ColorScheme is null && Motion is null;

	public string? ValueOf(string trait)
		=> trait switch
		{
			IconAppearanceTraits.ColorScheme => ColorScheme,
			IconAppearanceTraits.Motion => Motion,
			_ => null
		};
}
