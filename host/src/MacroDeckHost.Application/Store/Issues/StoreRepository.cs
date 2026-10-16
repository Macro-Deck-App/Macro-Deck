using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Store.Issues;

public sealed partial record StoreRepository
{
	private const string GitHubOrigin = "https://github.com/";

	public required string Owner { get; init; }

	public required string Name { get; init; }

	public string NewIssueUrl => $"{GitHubOrigin}{Owner}/{Name}/issues/new";

	public string CacheKey => $"{Owner}/{Name}".ToLowerInvariant();

	// Only the owner and name ever reach the GitHub API path, so both are held to GitHub's own
	// character rules rather than trusting whatever a registry manifest put in the URL.
	public static StoreRepository? Parse(string? repository)
	{
		if (!Uri.TryCreate(repository, UriKind.Absolute, out var uri)
			|| uri.Scheme != Uri.UriSchemeHttps
			|| !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length < 2)
		{
			return null;
		}

		var owner = segments[0];
		var name = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
			? segments[1][..^4]
			: segments[1];
		return OwnerPattern().IsMatch(owner) && NamePattern().IsMatch(name) && name is not ("." or "..")
			? new StoreRepository { Owner = owner, Name = name }
			: null;
	}

	public string TreeUrl(string reference)
	{
		var path = string.Join('/', reference.Split('/').Select(Uri.EscapeDataString));
		return $"{GitHubOrigin}{Owner}/{Name}/tree/{path}";
	}

	public bool IsIssueUrl(string? url, int number)
	{
		if (url is null || !url.StartsWith(GitHubOrigin, StringComparison.Ordinal))
		{
			return false;
		}

		var segments = url[GitHubOrigin.Length..].Split('/');
		return segments.Length == 4
			&& string.Equals(segments[0], Owner, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(segments[1], Name, StringComparison.OrdinalIgnoreCase)
			&& segments[2] == "issues"
			&& segments[3] == number.ToString(CultureInfo.InvariantCulture);
	}

	[GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9-]{0,38}\z")]
	private static partial Regex OwnerPattern();

	[GeneratedRegex(@"\A[A-Za-z0-9._-]{1,100}\z")]
	private static partial Regex NamePattern();
}
