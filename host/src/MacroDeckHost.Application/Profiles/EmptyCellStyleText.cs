using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Profiles;

public static class EmptyCellStyleText
{
	public const string Visible = "visible";

	public const string Transparent = "transparent";

	// Lenient on purpose: a value written by a newer host reads as unset instead of failing the load.
	public static EmptyCellStyle? Parse(string? value)
	{
		if (string.Equals(value, Visible, StringComparison.OrdinalIgnoreCase))
		{
			return EmptyCellStyle.Visible;
		}

		if (string.Equals(value, Transparent, StringComparison.OrdinalIgnoreCase))
		{
			return EmptyCellStyle.Transparent;
		}

		return null;
	}

	public static string? Format(EmptyCellStyle? style)
		=> style switch
		{
			EmptyCellStyle.Visible => Visible,
			EmptyCellStyle.Transparent => Transparent,
			_ => null
		};

	public static bool TryParseUpdate(string value, out EmptyCellStyle? style)
	{
		style = value.Length == 0 ? null : Parse(value);
		return value.Length == 0 || style is not null;
	}
}
