using System.Text.RegularExpressions;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// The client mirrors of this package's vocabulary are hand-written TypeScript, and nothing generates them.
/// Every one of them says so in its own header comment and then relies on a human to keep it true.
///
/// <para>
/// That trust failed once already: the UI model major moved to 3 in C# while
/// <c>ui-model-version.ts</c> still claimed 1-2, and because the desktop client negotiates that range
/// <i>before</i> opening a session, every configuration tree was declined and silently replaced by the
/// declared field list. Nothing was red. This fixture is the check that would have caught it, and it reads
/// the mirrors as <b>text</b> rather than restating them, so it can disagree with the code under test.
/// </para>
/// </summary>
[TestFixture]
public class TypeScriptMirrorDriftTests
{
	private const string _versionPath = "ui/runtime/src/ui-framework/ui-model-version.ts";
	private const string _primitivesPath = "ui/runtime/src/ui-config/config-primitives.ts";
	private const string _eventsPath = "ui/runtime/src/ui-config/config-events.ts";
	private const string _entryPointsPath = "ui/runtime/src/ui-config/config-entry-points.ts";
	private const string _propertiesPath = "ui/runtime/src/ui-config/config-properties.ts";

	[Test]
	public void The_client_speaks_the_same_ui_model_majors_this_package_does()
	{
		var mirrored = ReadNumericMembers(_versionPath, "UiModelVersions");

		Assert.Multiple(() =>
		{
			Assert.That(mirrored.GetValueOrDefault("Minimum"),
				Is.EqualTo(UiModelVersions.Minimum),
				$"'{_versionPath}' advertises a different oldest major than this package speaks");
			Assert.That(mirrored.GetValueOrDefault("Current"),
				Is.EqualTo(UiModelVersions.Current),
				$"'{_versionPath}' advertises a different newest major than this package speaks");
		});
	}

	[Test]
	public void The_client_mirrors_the_configuration_primitives_in_declaration_order()
		=> AssertMirrors(_primitivesPath, "UiConfigPrimitives", UiConfigPrimitives.WellKnown);

	[Test]
	public void The_client_mirrors_the_configuration_events_in_declaration_order()
		=> AssertMirrors(_eventsPath, "UiConfigEvents", UiConfigEvents.WellKnown);

	[Test]
	public void The_client_mirrors_the_configuration_entry_points_in_declaration_order()
		=> AssertMirrors(_entryPointsPath, "UiConfigEntryPoints", UiConfigEntryPoints.WellKnown);

	[Test]
	public void The_client_mirrors_the_configuration_property_keys_in_declaration_order()
		=> AssertMirrors(_propertiesPath, "UiConfigProperties", UiConfigProperties.WellKnown);

	private static void AssertMirrors(string path, string constName, IReadOnlyList<string> declared)
	{
		var mirrored = ReadStringMembers(path, constName);

		Assert.That(mirrored,
			Is.EqualTo(declared),
			$"'{path}' has drifted from {constName}. The mirror carries the values a client negotiates and " +
			"renders with, so a difference is a silently unrenderable tree rather than a build error.");
	}

	/// <summary>
	/// The values of an <c>export const X = {{ Member: 'value', … }} as const;</c> object, in the order they
	/// are written. Every mirror this fixture reads has that shape, and the order is the contract: the
	/// well-known lists on both sides are documented as being in declaration order.
	/// </summary>
	private static List<string> ReadStringMembers(string path, string constName)
	{
		var body = ReadObjectBody(path, constName);

		return Regex.Matches(body,
				@"^\s*(?<member>[A-Za-z][A-Za-z0-9]*)\s*:\s*'(?<value>[^']*)'\s*,",
				RegexOptions.Multiline)
			.Select(match => match.Groups["value"].Value)
			.ToList();
	}

	private static Dictionary<string, int> ReadNumericMembers(string path, string constName)
	{
		var body = ReadObjectBody(path, constName);

		return Regex.Matches(body,
				@"^\s*(?<member>[A-Za-z][A-Za-z0-9]*)\s*:\s*(?<value>-?\d+)\s*,",
				RegexOptions.Multiline)
			.ToDictionary(match => match.Groups["member"].Value,
				match => int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture),
				StringComparer.Ordinal);
	}

	private static string ReadObjectBody(string path, string constName)
	{
		var full = Path.Combine(FindRepositoryRoot(), path);

		Assert.That(File.Exists(full), Is.True, $"Could not find the client mirror at '{full}'.");

		var source = File.ReadAllText(full);
		var start = source.IndexOf($"export const {constName} = {{", StringComparison.Ordinal);

		Assert.That(start,
			Is.GreaterThanOrEqualTo(0),
			$"'{path}' no longer declares '{constName}', so this test is not reading the mirror it verifies");

		var end = source.IndexOf("} as const;", start, StringComparison.Ordinal);

		Assert.That(end,
			Is.GreaterThan(start),
			$"'{constName}' in '{path}' is not the 'as const' object literal this test knows how to read");

		return source[start..end];
	}

	/// <summary>Walks up from the test binaries to the repository root, the way the vocabulary's own parity
	/// test locates the client sources it reads.</summary>
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
