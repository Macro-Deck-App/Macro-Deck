using System.Text.RegularExpressions;

namespace MacroDeckHost.Ui;

/// <summary>
/// Device-specific Web Client builds packaged under <c>wwwroot/targets/&lt;id&gt;</c> (issue #727).
/// Each is an ordinary SPA with its own shell and base href, so it needs its own fallback route in
/// the same way <c>/admin</c> and <c>/legacy</c> do.
/// </summary>
internal static partial class WebClientTargets
{
	internal const string RootDirectoryName = "targets";

	/// <summary>
	/// The target directories present in this installation, sorted for a stable route order.
	/// Returns nothing when the client is not packaged at all, which is the ordinary development case.
	/// </summary>
	internal static IReadOnlyList<string> Discover(string? webRootPath)
	{
		if (string.IsNullOrEmpty(webRootPath))
		{
			return [];
		}

		var root = Path.Combine(webRootPath, RootDirectoryName);
		if (!Directory.Exists(root))
		{
			return [];
		}

		return Directory.EnumerateDirectories(root)
			.Select(Path.GetFileName)
			.Where(id => !string.IsNullOrEmpty(id) && IsValidId(id))
			// A directory without a shell cannot answer a navigation, and mapping it would shadow the
			// static files below it with a 404 that looks like a broken build rather than a missing one.
			.Where(id => File.Exists(Path.Combine(root, id!, "index.html")))
			.Select(id => id!)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>
	/// Ids become route templates and file paths, so the character set is closed rather than merely
	/// sanitised: nothing here may introduce a path segment, a traversal or a route parameter.
	/// </summary>
	internal static bool IsValidId(string id) => IdRegex().IsMatch(id);

	[GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$")]
	private static partial Regex IdRegex();
}
