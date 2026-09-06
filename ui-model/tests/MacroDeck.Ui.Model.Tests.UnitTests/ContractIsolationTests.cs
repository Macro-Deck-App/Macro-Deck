using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Ui.Model.Tests.UnitTests;

/// <summary>
/// The structural guards for the surface-agnosticism acceptance criterion: the assembly declares no
/// configuration or widget profile vocabulary, no public property is an enum, and the project
/// references nothing.
/// </summary>
[TestFixture]
public partial class ContractIsolationTests
{
	[GeneratedRegex("field|form|step|wizard|section|label|submit|chart|image|progress", RegexOptions.IgnoreCase)]
	private static partial Regex ProfileVocabularyPattern();

	private const BindingFlags AllDeclaredProperties =
		BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	[Test]
	public void The_core_declares_no_configuration_profile_vocabulary()
	{
		var assembly = typeof(UiNode).Assembly;
		var offendingTypeNames = assembly.GetExportedTypes()
			.Where(type => !IsReaderResolvedReference(type) && ProfileVocabularyPattern().IsMatch(type.Name))
			.Select(type => type.FullName)
			.ToList();

		var offendingPropertyNames = assembly.GetExportedTypes()
			.Where(type => !IsReaderResolvedReference(type))
			.SelectMany(type => type.GetProperties(AllDeclaredProperties))
			.Where(property => ProfileVocabularyPattern().IsMatch(property.Name))
			.Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(offendingTypeNames, Is.Empty);
			Assert.That(offendingPropertyNames, Is.Empty);
		});
	}

	/// <summary>
	/// The reader-resolved reference value shapes, which live here <b>deliberately</b>: ADR 0064 put
	/// <c>UiTimeReference</c> beside <c>UiResource</c> rather than in the profile package, and
	/// ADR 0064 put <c>UiProgressReference</c> next to it for the same reason - a reference is a
	/// property <i>value</i> shape the core has to be able to write, not a node type or a property key.
	///
	/// <para>
	/// Exempted by shape rather than by name so a third reference is covered too, and narrowly: only a
	/// <c>*Reference</c> under <c>References</c> and its own converter. Everything else the pattern names -
	/// a field, a form, a chart, a progress <i>indicator</i> - is still a profile's vocabulary and still
	/// has no business in the core.
	/// </para>
	/// </summary>
	private static bool IsReaderResolvedReference(Type type)
		=> (type.Namespace == "MacroDeck.Ui.Model.References" &&
				type.Name.EndsWith("Reference", StringComparison.Ordinal)) ||
			(type.Namespace == "MacroDeck.Ui.Model.Serialization" &&
				type.Name.EndsWith("ReferenceJsonConverter", StringComparison.Ordinal));

	[Test]
	public void No_public_property_is_an_enum()
	{
		var assembly = typeof(UiNode).Assembly;
		var enumProperties = assembly.GetExportedTypes()
			.SelectMany(type => type.GetProperties(AllDeclaredProperties))
			.Where(property => property.PropertyType.IsEnum ||
				(Nullable.GetUnderlyingType(property.PropertyType)?.IsEnum ?? false))
			.Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")
			.ToList();

		Assert.That(enumProperties, Is.Empty);
	}

	[Test]
	public void The_model_project_references_nothing()
	{
		var csprojPath = FindCsproj();
		var document = XDocument.Load(csprojPath);

		var projectReferences = document.Descendants("ProjectReference").ToList();
		var packageReferences = document.Descendants("PackageReference").ToList();

		Assert.Multiple(() =>
		{
			Assert.That(projectReferences, Is.Empty, "The model project must carry zero ProjectReferences.");
			Assert.That(packageReferences, Is.Empty, "The model project must carry zero PackageReferences.");
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
				"MacroDeck.Ui.Model",
				"MacroDeck.Ui.Model.csproj");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			"Could not locate MacroDeck.Ui.Model.csproj by walking up from AppContext.BaseDirectory " +
			$"('{AppContext.BaseDirectory}').");
	}
}
