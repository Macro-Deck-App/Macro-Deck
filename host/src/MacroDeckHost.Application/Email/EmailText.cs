namespace MacroDeckHost.Application.Email;

public static class EmailText
{
	public static string Clip(string? text, int maxLength)
	{
		if (text is null || maxLength <= 0)
		{
			return string.Empty;
		}

		if (text.Length <= maxLength)
		{
			return text;
		}

		var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
		return text[..length];
	}

	public static string? ClipOptional(string? text, int maxLength)
	{
		return string.IsNullOrWhiteSpace(text) ? null : Clip(text.Trim(), maxLength);
	}
}
