using System.Xml.Linq;

namespace MacroDeck.Ui.Testing.Tests.UnitTests;

/// <summary>
/// The structural guard against the test host leaking a dependency - an assertion library in
/// particular - into a published package: it must reference nothing but the DSL, and carry no
/// package dependency at all.
/// </summary>
[TestFixture]
public class ContractIsolationTests
{
	[Test]
	public void The_testing_project_references_only_the_dsl()
	{
		var csprojPath = FindCsproj();
		var document = XDocument.Load(csprojPath);

		var projectReferences = document.Descendants("ProjectReference").ToList();
		var packageReferences = document.Descendants("PackageReference").ToList();

		Assert.Multiple(() =>
		{
			Assert.That(projectReferences, Has.Count.EqualTo(1));
			Assert.That(projectReferences[0].Attribute("Include")?.Value,
				Does.EndWith("MacroDeck.Ui.csproj"));
			Assert.That(packageReferences, Is.Empty, "The testing project must carry zero PackageReferences.");
		});
	}

	/// <summary>Fails loudly rather than letting a missing file pass the isolation check vacuously.</summary>
	private static string FindCsproj()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null)
		{
			var candidate = Path.Combine(directory.FullName,
				"ui-model",
				"src",
				"MacroDeck.Ui.Testing",
				"MacroDeck.Ui.Testing.csproj");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			"Could not locate MacroDeck.Ui.Testing.csproj by walking up from AppContext.BaseDirectory " +
			$"('{AppContext.BaseDirectory}').");
	}
}
