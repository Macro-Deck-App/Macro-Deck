using System.Text;

namespace MacroDeck.LicenseTool.Model;

internal static class TextNormalizer
{
	public static string Normalize(string content)
	{
		var lines = content
			.TrimStart('﻿')
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n')
			.Split('\n')
			.Select(line => line.TrimEnd())
			.ToList();

		while (lines.Count > 0 && lines[0].Length == 0)
		{
			lines.RemoveAt(0);
		}

		while (lines.Count > 0 && lines[^1].Length == 0)
		{
			lines.RemoveAt(lines.Count - 1);
		}

		var builder = new StringBuilder();
		var blank = false;
		foreach (var line in lines)
		{
			if (line.Length == 0)
			{
				blank = true;
				continue;
			}

			if (blank)
			{
				builder.Append('\n');
				blank = false;
			}

			builder.Append(line).Append('\n');
		}

		return builder.ToString().TrimEnd('\n');
	}

	public static string? CleanUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}

		var cleaned = url.Trim();
		if (cleaned.StartsWith("git+", StringComparison.Ordinal))
		{
			cleaned = cleaned[4..];
		}

		if (cleaned.StartsWith("git://", StringComparison.Ordinal))
		{
			cleaned = "https://" + cleaned[6..];
		}

		if (cleaned.StartsWith("git@github.com:", StringComparison.Ordinal))
		{
			cleaned = "https://github.com/" + cleaned["git@github.com:".Length..];
		}

		if (cleaned.EndsWith(".git", StringComparison.Ordinal))
		{
			cleaned = cleaned[..^4];
		}

		return cleaned.TrimEnd('/');
	}
}
