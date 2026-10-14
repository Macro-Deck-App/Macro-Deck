using System.Reflection;
using System.Xml.Linq;

namespace MacroDeckHost.Tests.UnitTests.Architecture;

[TestFixture]
internal sealed class ProjectReferenceTests
{
	private static readonly string[] _integrationsMayReference =
	[
		"MacroDeck.Sdk",
		"MacroDeck.Ui",
		"MacroDeck.Ui.Model",
		"MacroDeck.Localization",
		"MacroDeckHost.Localization",
		"MacroDeckHost.Platform"
	];

	[Test]
	public void Built_in_integrations_reference_only_the_SDK_the_UI_DSL_localization_and_the_platform_library()
	{
		var references = ReferencesOf("MacroDeckHost.Integrations");

		Assert.That(references.Where(IsMacroDeck).Except(_integrationsMayReference), Is.Empty);
	}

	[Test]
	public void The_platform_library_references_neither_the_SDK_nor_any_host_project()
	{
		var references = ReferencesOf("MacroDeckHost.Platform");

		Assert.That(references.Where(name
				=> name == "MacroDeck.Sdk" || name.StartsWith("MacroDeckHost", StringComparison.Ordinal)),
			Is.Empty);
	}

	private static bool IsMacroDeck(string name)
	{
		return name.StartsWith("MacroDeck", StringComparison.Ordinal);
	}

	private static HashSet<string> ReferencesOf(string projectName)
	{
		var compiled = Assembly.Load(projectName)
			.GetReferencedAssemblies()
			.Select(reference => reference.Name!);
		var declared = XDocument.Load(ProjectFile(projectName))
			.Descendants("ProjectReference")
			.Select(reference => Path.GetFileNameWithoutExtension(
				reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));

		return compiled.Concat(declared).ToHashSet(StringComparer.Ordinal);
	}

	private static string ProjectFile(string projectName)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("MacroDeck.slnx not found"),
			"host",
			"src",
			projectName,
			$"{projectName}.csproj");
	}
}
