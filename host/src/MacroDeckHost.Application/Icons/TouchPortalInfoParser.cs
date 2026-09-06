namespace MacroDeckHost.Application.Icons;

public sealed record TouchPortalPackInfo(string? Name, string? Author, string? Link, string? BgColor);

public static class TouchPortalInfoParser
{
	public static TouchPortalPackInfo Parse(Stream content)
	{
		string? name = null, author = null, link = null, bgColor = null;
		using var reader = new StreamReader(content);
		while (reader.ReadLine() is { } line)
		{
			var separator = line.IndexOf('=');
			if (separator <= 0)
			{
				continue;
			}

			var key = line[..separator].Trim();
			var value = line[(separator + 1)..].Trim();
			if (value.Length == 0)
			{
				continue;
			}

			if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
			{
				name = value;
			}
			else if (key.Equals("author", StringComparison.OrdinalIgnoreCase))
			{
				author = value;
			}
			else if (key.Equals("link", StringComparison.OrdinalIgnoreCase))
			{
				link = value;
			}
			else if (key.Equals("bgcolor", StringComparison.OrdinalIgnoreCase))
			{
				bgColor = value;
			}
		}

		return new TouchPortalPackInfo(name, author, link, bgColor);
	}
}
