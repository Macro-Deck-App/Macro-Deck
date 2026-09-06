using System.Globalization;
using MacroDeck.Plugin.Analyzers.Rules;
using static MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support.LocalizationGeneratorTestHarness;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// MDLOC006. A retired catalog key keeps compiling and keeps rendering - deleting it would leave a call
/// site with a bare "member does not exist" and nothing to migrate to - so the retirement has to surface
/// as a diagnostic at the call site instead.
/// </summary>
[TestFixture]
public class RemovedLocalizationKeyTests
{
	private static readonly string[] _onlyMdloc006 = ["MDLOC006"];

	private const string _callSite = """
									 namespace TestPlugin
									 {
									 	internal static class CallSite
									 	{
									 		public static void Use()
									 		{
									 			var retired = Strings.Retired();
									 			var current = Strings.Current();
									 		}
									 	}
									 }
									 """;

	private static string Generate(string retiredComment)
	{
		var run = Run(new Dictionary<string, string>
		{
			[DefaultFileName] = Resx(Entry("Retired", "Old wording", retiredComment) +
				"\n" +
				Entry("Current", "New wording")),
		});

		Assert.That(run.Ids, Is.Empty, "a retirement marker is not itself a defect");
		Assert.That(run.GeneratedSource, Is.Not.Null);

		return run.GeneratedSource!;
	}

	[Test]
	public async Task Calling_a_retired_key_reports_it_with_the_replacement_to_move_to()
	{
		var generated = Generate("[removed:Use Common.Apply instead.]");

		var diagnostics = await AnalyzeCallSiteAsync(generated,
			_callSite,
			new RemovedLocalizationKeyAnalyzer());

		Assert.Multiple(() =>
		{
			// Exactly one: the current key beside it must not be reported.
			Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(_onlyMdloc006));
			Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture), Does.Contain("Retired"));
			Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
				Does.Contain("Use Common.Apply instead."));
		});
	}

	[Test]
	public async Task A_catalog_with_nothing_retired_reports_nothing()
	{
		var generated = Generate("Just a translator note.");

		var diagnostics = await AnalyzeCallSiteAsync(generated,
			_callSite,
			new RemovedLocalizationKeyAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}

	[Test]
	public void A_retired_key_still_resolves_so_existing_call_sites_keep_rendering_text()
	{
		var generated = Generate("[removed:Use Common.Apply instead.]");

		Assert.That(generated, Does.Contain("templates[\"Retired\"] = \"Old wording\";"));
	}
}
