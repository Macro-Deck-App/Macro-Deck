using System.Text.RegularExpressions;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class IntegrationHttpSourceGuardTests
{
	private static readonly Regex[] _forbidden =
	[
		new(@"new\s+HttpClient\b", RegexOptions.Compiled),
		new(@"\bHttpClient\??\s+\w+\s*=\s*new\s*\(", RegexOptions.Compiled),
		new(@"\bHttpClient\s*>?\s*\w*\s*\{[^}]*\}\s*=\s*new\s*\(", RegexOptions.Compiled),
		new(@"new\s+SocketsHttpHandler\b", RegexOptions.Compiled),
		new(@"new\s+HttpClientHandler\b", RegexOptions.Compiled),
		new(@"new\s+ClientWebSocket\b", RegexOptions.Compiled),
		new(@"DefaultRequestHeaders\.UserAgent", RegexOptions.Compiled)
	];

	// Libraries that build their own transport with no injection point.
	private static readonly string[] _libraryOwnedTransport = ["Obs/ObsClient.cs", "Twitch/Protocol/TwitchHelixClient.cs"];

	[Test]
	public void Integration_code_builds_its_transports_through_the_shared_factory()
	{
		var root = FindIntegrationsDirectory();

		var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
			.Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
			.Where(relative => relative != "Http/IntegrationHttp.cs")
			.Where(relative => !_libraryOwnedTransport.Contains(relative))
			.Where(relative => _forbidden.Any(pattern => pattern.IsMatch(File.ReadAllText(Path.Combine(root, relative)))))
			.ToList();

		Assert.That(offenders, Is.Empty,
			"These files build an HTTP or WebSocket transport directly instead of through IntegrationHttp, " +
			"so the configured User-Agent would not reach them.");
	}

	[Test]
	public void The_guard_recognises_the_target_typed_forms_that_were_missed_before()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_forbidden.Any(p => p.IsMatch("private static readonly HttpClient _http = new() { Timeout = x };")), Is.True);
			Assert.That(_forbidden.Any(p => p.IsMatch("var client = new HttpClient { Timeout = x };")), Is.True);
			Assert.That(_forbidden.Any(p => p.IsMatch("var socket = new ClientWebSocket();")), Is.True);
			Assert.That(_forbidden.Any(p => p.IsMatch("CreateClient(new SocketsHttpHandler(), true)")), Is.True);
		});
	}

	private static string FindIntegrationsDirectory()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(directory.FullName, "host", "src", "MacroDeckHost.Integrations");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException("Could not locate host/src/MacroDeckHost.Integrations above the test output.");
	}
}
