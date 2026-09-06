using System.Text.RegularExpressions;
using MacroDeck.Ui.Config;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// The independent verifier behind the primitive vocabulary: the first twenty-seven input type strings have
/// to be exactly the control names the existing editor's parameter mapping produces, one to one, so a
/// renderer's mapping from a node type to a control is the identity function over the parameter types.
///
/// <para>
/// It says nothing about how many types the vocabulary holds in total. The profile also ships Macro Deck's
/// own high-level controls, which name editors the application already has rather than parameter types and
/// therefore have no counterpart to be in parity with; asserting a total would only mean this test had to be
/// edited every time one was added, which is not a check.
/// </para>
///
/// <para>
/// The expectation is read out of <c>action-parameter-mapping.util.ts</c> as <b>text</b>, on purpose. A
/// hard-coded map copied from <see cref="UiConfigPrimitives" /> would restate the constants and pass whatever
/// they said; a project reference to the SDK would make the DSL package depend on it, which its own isolation
/// test forbids. Reading the file the editor actually switches on is the only version of this test that can
/// disagree with the code under test.
/// </para>
/// </summary>
[TestFixture]
public class ParameterTypeParityTests
{
	private const string _mappingPath =
		"ui/angular/projects/desktop-ui/src/app/domain/action-parameter-mapping.util.ts";

	[Test]
	public void The_input_primitives_are_the_control_names_the_editor_mapping_produces()
	{
		var controlNames = ReadControlNames();

		// The inputs come first in the declared vocabulary; the fourteen chrome names have no counterpart in the
		// mapping, because the existing editor renders the chrome as markup rather than through a control name.
		var declaredInputs = UiConfigPrimitives.WellKnown.Take(controlNames.Count).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(controlNames,
				Has.Count.EqualTo(27),
				"the mapping produces one control name per action parameter type, plus the string fallback");
			Assert.That(declaredInputs, Is.EquivalentTo(controlNames));

			foreach (var name in controlNames)
			{
				Assert.That(UiConfigPrimitives.WellKnown,
					Does.Contain(name),
					$"the mapping produces '{name}', which this vocabulary has to ship");
			}
		});
	}

	/// <summary>Reads the distinct control names the mapping function returns. Every branch of it is a
	/// <c>return '&lt;name&gt;';</c>, including the default that falls back to the text field, and two branches
	/// deliberately share a name for a legacy wire spelling.</summary>
	private static HashSet<string> ReadControlNames()
	{
		var path = Path.Combine(FindRepositoryRoot(), _mappingPath);

		Assert.That(File.Exists(path), Is.True, $"Could not find the editor's parameter mapping at '{path}'.");

		var source = File.ReadAllText(path);
		var start = source.IndexOf("export function mapActionParamType", StringComparison.Ordinal);

		Assert.That(start,
			Is.GreaterThanOrEqualTo(0),
			"the mapping function was renamed, so this test is no longer reading the mapping it verifies");

		var body = source[start..];
		var names = new HashSet<string>(StringComparer.Ordinal);

		foreach (var match in Regex.Matches(body, @"return '(?<name>[a-z-]+)';").Cast<Match>())
		{
			names.Add(match.Groups["name"].Value);
		}

		return names;
	}

	/// <summary>Walks up from the test binaries to the repository root, the way the model's own isolation test
	/// locates its project file.</summary>
	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException(
			"Could not locate the repository root by walking up from AppContext.BaseDirectory " +
			$"('{AppContext.BaseDirectory}').");
	}
}
