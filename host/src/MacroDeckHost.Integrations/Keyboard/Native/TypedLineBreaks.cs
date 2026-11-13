namespace MacroDeckHost.Integrations.Keyboard.Native;

internal static class TypedLineBreaks
{
	public static void Walk(string text, Action<string> typeText, Action lineBreak)
	{
		var start = 0;
		for (var i = 0; i < text.Length; i++)
		{
			if (text[i] is not ('\r' or '\n'))
			{
				continue;
			}

			if (i > start)
			{
				typeText(text[start..i]);
			}

			lineBreak();
			if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
			{
				i++;
			}

			start = i + 1;
		}

		if (start < text.Length)
		{
			typeText(text[start..]);
		}
	}
}
