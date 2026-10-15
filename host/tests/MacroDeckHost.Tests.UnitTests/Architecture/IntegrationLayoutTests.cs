using System.Text.RegularExpressions;

namespace MacroDeckHost.Tests.UnitTests.Architecture;

[TestFixture]
internal sealed partial class IntegrationLayoutTests
{
	private const string IntegrationsNamespace = "MacroDeckHost.Integrations";

	private static readonly string[] _sharedFolders = ["Streaming", "Ui"];

	private static readonly string[] _vocabulary =
	[
		"Actions",
		"Auth",
		"Chat",
		"Configuration",
		"Connections",
		"Events",
		"Icons",
		"Migration",
		"Models",
		"Native",
		"Protocol",
		"Providers",
		"State",
		"Variables",
		"Widgets"
	];

	private static readonly Dictionary<string, string[]> _extraFolders = new()
		{ ["System"] = ["Metrics", "Power", "Volume"] };

	[Test]
	public void The_root_of_an_integration_holds_only_its_entry_point()
	{
		var strays = IntegrationFolders()
			.SelectMany(folder => Directory.EnumerateFiles(folder)
				.Where(path => Path.GetFileName(path) is { } name
					&& !name.StartsWith('.')
					&& !EntryPoint().IsMatch(name))
				.Select(RelativeToIntegrations))
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.That(strays,
			Is.Empty,
			"Only <Something>Integration.cs and its partial parts belong in an integration's root. Move the rest "
			+ "into the folder for its responsibility (contributing-integrations.md, Folder layout).");
	}

	[Test]
	public void An_integration_folder_uses_only_the_documented_folder_names()
	{
		var unknown = IntegrationFolders()
			.SelectMany(folder => Directory.EnumerateDirectories(folder)
				.Select(path => Path.GetFileName(path))
				.Where(name => name is not ("obj" or "bin"))
				.Where(name => !_vocabulary.Contains(name)
					&& !_extraFolders.GetValueOrDefault(Path.GetFileName(folder), []).Contains(name))
				.Select(name => $"{Path.GetFileName(folder)}/{name}"))
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.That(unknown,
			Is.Empty,
			"Use the folder names of the layout convention in contributing-integrations.md instead of a new one.");
	}

	[Test]
	public void Folders_are_not_nested_except_for_the_icon_sets_the_host_resolves_by_key()
	{
		var nested = IntegrationFolders()
			.SelectMany(folder => Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
				.Where(relative => !relative.StartsWith("obj", StringComparison.Ordinal)
					&& !relative.StartsWith("bin", StringComparison.Ordinal))
				.Where(relative => relative.Contains('/') && relative.Split('/') is not ["Icons", _])
				.Select(relative => $"{Path.GetFileName(folder)}/{relative}"))
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.That(nested, Is.Empty);
	}

	[Test]
	public void The_namespace_of_a_source_file_follows_its_folder()
	{
		var root = FindIntegrationsDirectory();

		var mismatches = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(path => !IsBuildOutput(root, path))
			.Select(path => (Path: path, Actual: DeclaredNamespace(path), Expected: ExpectedNamespace(root, path)))
			.Where(entry => entry.Actual != entry.Expected)
			.Select(entry => $"{RelativeToIntegrations(entry.Path)}: {entry.Actual} (expected {entry.Expected})")
			.Order(StringComparer.Ordinal)
			.ToList();

		Assert.That(mismatches, Is.Empty);
	}

	private static List<string> IntegrationFolders()
	{
		return Directory.EnumerateDirectories(FindIntegrationsDirectory())
			.Where(path => Path.GetFileName(path) is not ("obj" or "bin"))
			.Where(path => !_sharedFolders.Contains(Path.GetFileName(path)))
			.Order(StringComparer.Ordinal)
			.ToList();
	}

	private static bool IsBuildOutput(string root, string path)
	{
		return Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)[0] is "obj" or "bin";
	}

	private static string RelativeToIntegrations(string path)
	{
		return Path.GetRelativePath(FindIntegrationsDirectory(), path).Replace('\\', '/');
	}

	private static string ExpectedNamespace(string root, string path)
	{
		var folder = Path.GetDirectoryName(Path.GetRelativePath(root, path))!;
		return folder.Length == 0
			? IntegrationsNamespace
			: IntegrationsNamespace + "." + folder.Replace(Path.DirectorySeparatorChar, '.');
	}

	private static string DeclaredNamespace(string path)
	{
		return DeclaredNamespaceLine().Match(File.ReadAllText(path)).Groups[1].Value;
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

		throw new InvalidOperationException(
			"Could not locate host/src/MacroDeckHost.Integrations above the test output.");
	}

	[GeneratedRegex(@"^\w+Integration(\.\w+)?\.cs$")]
	private static partial Regex EntryPoint();

	[GeneratedRegex(@"^namespace\s+([\w.]+)\s*;", RegexOptions.Multiline)]
	private static partial Regex DeclaredNamespaceLine();
}
