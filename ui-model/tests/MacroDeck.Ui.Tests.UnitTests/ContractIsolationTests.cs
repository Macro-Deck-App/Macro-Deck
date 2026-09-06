using System.Xml.Linq;

namespace MacroDeck.Ui.Tests.UnitTests;

/// <summary>
/// The structural guard against the DSL project growing a dependency every plugin author would then
/// inherit: it references the model and the localization package, nothing else, and carries no package
/// dependency at all.
/// </summary>
[TestFixture]
public class ContractIsolationTests
{
	// MacroDeck.Localization joined the model here when text properties became localizable (#326). It
	// earns its place on the same terms as the model: it is plugin-facing by design and itself references
	// nothing, so it adds no transitive dependency to anything a plugin author consumes. Any third entry
	// needs the same argument made for it.
	private static readonly string[] _allowedReferences =
		["MacroDeck.Ui.Model.csproj", "MacroDeck.Localization.csproj"];

	[Test]
	public void The_dsl_project_references_only_the_model_and_localization()
	{
		var csprojPath = FindCsproj();
		var document = XDocument.Load(csprojPath);

		var projectReferences = document.Descendants("ProjectReference")
			.Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
			.Select(include => include.Split('\\', '/')[^1])
			.ToList();

		var packageReferences = document.Descendants("PackageReference").ToList();

		Assert.Multiple(() =>
		{
			Assert.That(projectReferences, Is.EquivalentTo(_allowedReferences));
			Assert.That(packageReferences, Is.Empty, "The DSL project must carry zero PackageReferences.");
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
				"MacroDeck.Ui",
				"MacroDeck.Ui.csproj");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			"Could not locate MacroDeck.Ui.csproj by walking up from AppContext.BaseDirectory " +
			$"('{AppContext.BaseDirectory}').");
	}
}
