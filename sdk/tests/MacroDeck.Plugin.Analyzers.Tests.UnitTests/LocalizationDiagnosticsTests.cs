using static MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support.LocalizationGeneratorTestHarness;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// The "placeholder inconsistencies between languages produce build diagnostics" acceptance criterion,
/// and its five siblings.
///
/// <para>
/// One matrix rather than six independent tests, and every case asserts the <b>exact</b> set of ids
/// rather than that a particular id is present. An implementation that reports all six diagnostics for
/// every defect satisfies any "contains MDLOC00x" assertion, and each rule's near-miss case - a
/// translation that legitimately omits a key, placeholders in a different order, the same key in two
/// files - is what stops a rule from firing on correct resources.
/// </para>
/// </summary>
[TestFixture]
public class LocalizationDiagnosticsTests
{
	private const string GermanFileName = "Strings.de.resx";

	public static IEnumerable<TestCaseData> Cases()
	{
		yield return Case("A resource set with nothing wrong with it",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect") +
					"\n" +
					Entry("ConnectedAs", "Connected as {userName}")),
				[GermanFileName] = Resx(Entry("Connect", "Verbinden") +
					"\n" +
					Entry("ConnectedAs", "Verbunden als {userName}")),
			});

		yield return Case("A key translated but absent from the default language",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect")),
				[GermanFileName] = Resx(Entry("Connect", "Verbinden") + "\n" + Entry("Extra", "Zusatz")),
			},
			"MDLOC001");

		yield return Case("A key present in the default language and simply not translated yet",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect") + "\n" + Entry("Later", "Later")),
				[GermanFileName] = Resx(Entry("Connect", "Verbinden")),
			});

		yield return Case("A translation naming a placeholder the default language does not have",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("ConnectedAs", "Connected as {userName}")),
				[GermanFileName] = Resx(Entry("ConnectedAs", "Verbunden als {name}")),
			},
			"MDLOC002");

		yield return Case("A translation using the same placeholders in a different order",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Range", "{minimum} to {maximum}")),
				[GermanFileName] = Resx(Entry("Range", "{maximum} ab {minimum}")),
			});

		// The case a count comparison passes and a set comparison catches: two placeholders either side,
		// but the German text drops {b} and repeats {a}, so a caller's second argument vanishes.
		yield return Case("A translation with the same number of placeholders but a different set",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Pair", "{a} and {b}")),
				[GermanFileName] = Resx(Entry("Pair", "{a} und {a}")),
			},
			"MDLOC002");

		yield return Case("The same key declared twice in one file",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect") + "\n" + Entry("Connect", "Connect again")),
			},
			"MDLOC003");

		yield return Case("The same key in the default language and in a translation",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect")),
				[GermanFileName] = Resx(Entry("Connect", "Verbinden")),
			});

		yield return Case("A parameter declared as a type the compiler cannot emit",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("DeviceCount", "{count} devices found", "[count:flibble]")),
			},
			"MDLOC004");

		yield return Case("A parameter declared for a placeholder the template does not use",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("DeviceCount", "{count} devices found", "[total:int]")),
			},
			"MDLOC004");

		yield return Case("A parameter declared as each supported type",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("A", "{v}", "[v:string]") +
					"\n" +
					Entry("B", "{v}", "[v:int]") +
					"\n" +
					Entry("C", "{v}", "[v:long]") +
					"\n" +
					Entry("D", "{v}", "[v:double]") +
					"\n" +
					Entry("E", "{v}", "[v:bool]")),
			});

		yield return Case("A resource file whose culture suffix is not a culture name",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect")),
				["Strings.de_DE.resx"] = Resx(Entry("Connect", "Verbinden")),
			},
			"MDLOC005");

		yield return Case("A plural family with both its forms",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Icons.One", "{count} icon", "[plural]") +
					"\n" +
					Entry("Icons.Other", "{count} icons", "[plural]")),
				[GermanFileName] = Resx(Entry("Icons.One", "{count} Symbol", "[plural]") +
					"\n" +
					Entry("Icons.Other", "{count} Symbole", "[plural]")),
			});

		// The form a family cannot do without: every count other than one resolves through Other, so a
		// family carrying only the singular answers nothing.
		yield return Case("A plural family missing its Other form",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Icons.One", "{count} icon", "[plural]")),
			},
			"MDLOC007");

		yield return Case("A plural entry whose key does not end in a form",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Icons.Several", "{count} icons", "[plural]")),
			},
			"MDLOC007");

		// Both are true of this resource set, and each names a different half of the problem: the family
		// cannot be built, and what is left is a key that is also a group.
		yield return Case("A plural family whose base key is also an ordinary key",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Icons", "Icons") +
					"\n" +
					Entry("Icons.Other", "{count} icons", "[plural]")),
			},
			"MDLOC007",
			"MDLOC008");

		// The reason plural is opt-in rather than inferred from the key: a resource set that happens to
		// name keys this way keeps generating the members it always did.
		yield return Case("Keys ending in a form name without the plural declaration",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Chosen.One", "The chosen one") +
					"\n" +
					Entry("Chosen.Other", "Someone else")),
			});

		// A singular that does not print the count still needs it passed, so the caller cannot know which
		// form it will select. Dropping a placeholder the other form uses is not a mismatch.
		yield return Case("A plural form that leaves the count out of its text",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Icons.One", "one icon", "[plural]") +
					"\n" +
					Entry("Icons.Other", "{count} icons", "[plural]")),
				[GermanFileName] = Resx(Entry("Icons.One", "ein Symbol", "[plural]") +
					"\n" +
					Entry("Icons.Other", "{count} Symbole", "[plural]")),
			});

		yield return Case("A plural form naming a placeholder the family does not have",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Icons.One", "{count} icon", "[plural]") +
					"\n" +
					Entry("Icons.Other", "{count} icons", "[plural]")),
				[GermanFileName] = Resx(Entry("Icons.One", "{count} Symbol", "[plural]") +
					"\n" +
					Entry("Icons.Other", "{total} Symbole", "[plural]")),
			},
			"MDLOC002");

		// A dotted key becomes a nested class, so this pair would generate a method and a class of the
		// same name; caught here rather than as a duplicate-definition error in generated source.
		yield return Case("A key that is also the group other keys nest under",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Filters", "Filters") +
					"\n" +
					Entry("Filters.Date", "Date")),
			},
			"MDLOC008");

		yield return Case("A group that is not itself a key",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Filters.Date", "Date") +
					"\n" +
					Entry("Filters.Size", "Size")),
			});

		// .NET manufactures a CultureInfo for anything vaguely well-formed under ICU, so the check is on
		// the name's shape. These two are the shapes that check must not reject.
		yield return Case("Resource files for a regional and a script-qualified culture",
			new Dictionary<string, string>
			{
				[DefaultFileName] = Resx(Entry("Connect", "Connect")),
				["Strings.pt-BR.resx"] = Resx(Entry("Connect", "Conectar")),
				["Strings.zh-Hant-TW.resx"] = Resx(Entry("Connect", "連線")),
			});
	}

	[TestCaseSource(nameof(Cases))]
	public void The_generator_reports_exactly_the_diagnostics_a_resource_set_earns(
		Dictionary<string, string> files,
		string[] expectedIds)
	{
		var run = Run(files);

		Assert.That(run.Ids, Is.EquivalentTo(expectedIds));
	}

	[Test]
	public void A_project_with_no_resource_files_generates_nothing_and_reports_nothing()
	{
		var run = Run(new Dictionary<string, string>());

		Assert.Multiple(() =>
		{
			Assert.That(run.GeneratedSource, Is.Null);
			Assert.That(run.Ids, Is.Empty);
		});
	}

	[Test]
	public void A_duplicate_key_is_reported_against_the_entry_rather_than_the_project()
	{
		var run = Run(new Dictionary<string, string>
		{
			[DefaultFileName] = Resx(Entry("Connect", "Connect") + "\n" + Entry("Connect", "Connect again")),
		});

		var location = run.Diagnostics.Single().Location;

		Assert.Multiple(() =>
		{
			Assert.That(location.GetLineSpan().Path, Does.EndWith(DefaultFileName));
			Assert.That(location.SourceSpan.Length,
				Is.GreaterThan(0),
				"a resource diagnostic with an empty span points at the file, not at the key");
		});
	}

	private static TestCaseData Case(string name, Dictionary<string, string> files, params string[] expectedIds)
		=> new TestCaseData(files, expectedIds).SetName($"{{m}}: {name}");
}
