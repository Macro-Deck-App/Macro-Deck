using System.Text;
using MacroDeck.LicenseTool.Model;

namespace MacroDeck.LicenseTool.Output;

internal static class NoticeWriter
{
	public static string Write(string header, IEnumerable<(string Title, string Text)> notices)
	{
		var builder = new StringBuilder(TextNormalizer.Normalize(header)).Append('\n');
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (title, text) in notices
			.OrderBy(notice => notice.Title, StringComparer.OrdinalIgnoreCase)
			.ThenBy(notice => notice.Text, StringComparer.Ordinal))
		{
			var normalized = TextNormalizer.Normalize(text);
			if (normalized.Length == 0 || !seen.Add(normalized))
			{
				continue;
			}

			builder.Append('\n').Append(title).Append('\n').Append(new string('-', title.Length)).Append('\n')
				.Append(normalized).Append('\n');
		}

		return builder.ToString();
	}
}
