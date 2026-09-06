using System.Xml.Linq;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests;

/// <summary>
/// Proves the acceptance criterion "the contracts do not reference host implementation projects" by
/// reading the csproj as text and inspecting its references, rather than by
/// <see cref="System.Reflection.Assembly.GetReferencedAssemblies" />. Roslyn elides references to
/// assemblies whose types go unused, so the reflection-based version would pass vacuously in exactly
/// the case it needs to catch - a stray <c>ProjectReference</c> added before anyone writes code
/// against it.
/// </summary>
[TestFixture]
public class ContractIsolationTests
{
	[Test]
	public void The_contract_project_references_only_the_sdk_and_carries_no_package_references()
	{
		var csprojPath = FindContractCsproj();
		var document = XDocument.Load(csprojPath);

		var projectReferences = document.Descendants("ProjectReference")
			.Select(element => element.Attribute("Include")?.Value)
			.Where(value => value is not null)
			.ToList();

		var packageReferences = document.Descendants("PackageReference").ToList();

		Assert.Multiple(() =>
		{
			Assert.That(projectReferences, Has.Count.EqualTo(1), "Expected exactly one ProjectReference.");
			Assert.That(projectReferences[0], Does.Contain("MacroDeck.Sdk.csproj"));
			Assert.That(packageReferences, Is.Empty, "The contract project must carry zero PackageReferences.");
		});
	}

	[Test]
	public void The_csproj_can_be_located_by_walking_up_from_the_test_output_directory()
		=> Assert.DoesNotThrow(() => FindContractCsproj());

	/// <summary>Fails loudly rather than letting a missing file pass the isolation check vacuously.</summary>
	private static string FindContractCsproj()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null)
		{
			var candidate = Path.Combine(directory.FullName,
				"protocol",
				"src",
				"MacroDeck.Plugin.Protocol",
				"MacroDeck.Plugin.Protocol.csproj");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			"Could not locate MacroDeck.Plugin.Protocol.csproj by walking up from AppContext.BaseDirectory " +
			$"('{AppContext.BaseDirectory}').");
	}
}
