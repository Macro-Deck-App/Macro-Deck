using System.Globalization;

namespace MacroDeckHost.Application.Services;

public static class LocalizationCultureValidation
{
	public static bool IsValid(string? culture)
	{
		if (string.IsNullOrWhiteSpace(culture))
		{
			return false;
		}

		try
		{
			return !CultureInfo.GetCultureInfo(culture).Equals(CultureInfo.InvariantCulture);
		}
		catch (CultureNotFoundException)
		{
			return false;
		}
	}
}
