using MacroDeck.LicenseTool.Tests.UnitTests.Support;

namespace MacroDeck.LicenseTool.Tests.UnitTests;

public sealed class NoticesOutputTests
{
	private static readonly Dictionary<string, string[]> Native = new() { ["*"] = ["runtimes/any/native/lib.so"] };

	private static GenerationResult GenerateWithVersion(string version, string lineEnding = "\n")
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet("linux-x64");
		repository.NuGetPackage("First", version, "MIT", Native,
			new Dictionary<string, string> { ["LICENSE"] = $"Shared license text{lineEnding}{lineEnding}Copyright holder   {lineEnding}" });
		repository.NuGetPackage("Second", "1.0.0", "MIT", Native,
			new Dictionary<string, string> { ["LICENSE"] = "Shared license text\n\nCopyright holder\n" });
		return repository.Generate();
	}

	[Test]
	public void A_version_only_change_leaves_the_notices_unchanged()
	{
		var before = GenerateWithVersion("1.0.0");
		var after = GenerateWithVersion("1.0.1");

		Assert.Multiple(() =>
		{
			Assert.That(after.ThirdPartyNotices, Is.EqualTo(before.ThirdPartyNotices));
			Assert.That(after.ThirdPartyNotices, Does.Not.Contain("1.0.0"));
		});
	}

	[Test]
	public void Identical_texts_are_printed_once_with_every_component_they_cover()
	{
		var result = GenerateWithVersion("1.0.0", lineEnding: "\r\n");

		Assert.Multiple(() =>
		{
			Assert.That(result.ThirdPartyNotices, Does.Contain("[1] First, Second\n"));
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain("[2]"));
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain('\r'));
		});
	}

	[Test]
	public void Apache_notice_files_and_override_notices_go_into_NOTICE()
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet("linux-x64");
		repository.NuGetPackage("Apache.Package", "1.0.0", "Apache-2.0", Native,
			new Dictionary<string, string> { ["NOTICE.txt"] = "Apache.Package includes work by Example Org." });
		repository.NuGetPackage("Mit.Package", "1.0.0", "MIT", Native,
			new Dictionary<string, string> { ["NOTICE"] = "An MIT package notice." });
		repository.Overrides("""
			packages:
			  - ecosystem: nuget
			    name: Mit.Package
			    notice: Mit.Package is used under a special exemption.
			""");

		var result = repository.Generate();

		Assert.That(result.Notice, Is.EqualTo("""
			Fixture product

			Apache.Package
			--------------
			Apache.Package includes work by Example Org.

			Mit.Package
			-----------
			Mit.Package is used under a special exemption.

			"""));
	}

	[Test]
	public void Custom_licenses_need_a_reviewed_override()
	{
		GenerationResult Generate(string overrides)
		{
			using var repository = new FixtureRepository();
			repository.UseNuGet("linux-x64");
			repository.NuGetPackage("Custom", "1.0.0", "LicenseRef-Custom-EULA", Native,
				new Dictionary<string, string> { ["EULA.txt"] = "Custom terms" });
			repository.Overrides(overrides);
			return repository.Generate();
		}

		var unreviewed = Generate("packages: []");
		var reviewed = Generate("""
			packages:
			  - ecosystem: nuget
			    name: Custom
			    license: LicenseRef-Custom-EULA
			    licenseFile: packages/custom/1.0.0/EULA.txt
			    reviewed: Approved by the maintainers.
			""");

		Assert.Multiple(() =>
		{
			Assert.That(unreviewed.Problems, Has.Some.StartsWith("nuget Custom: license 'LicenseRef-Custom-EULA' is not allowed"));
			Assert.That(reviewed.Problems, Is.Empty);
			Assert.That(reviewed.ThirdPartyNotices, Does.Contain("Custom\n  License: LicenseRef-Custom-EULA\n"));
			Assert.That(reviewed.ThirdPartyNotices, Does.Contain("Custom terms"));
		});
	}

	[Test]
	public void Stale_overrides_and_attributions_fail()
	{
		using var repository = new FixtureRepository();
		repository.Write("assets/icon.svg", "<svg/>");
		repository.Overrides("""
			packages:
			  - ecosystem: npm
			    name: removed-package
			    license: MIT
			""");
		repository.Attributions("""
			assets:
			  - name: Kept icon
			    license: MIT
			    copyright: Icon Author
			    paths: [assets/*.svg]
			  - name: Removed icon
			    license: MIT
			    paths: [assets/removed/**/*.png]
			""");

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.Problems, Is.EquivalentTo(new[]
			{
				"attributions.yml: Removed icon: path 'assets/removed/**/*.png' matches no file; update or remove the attribution",
				"overrides.yml: npm removed-package matches no shipped component; remove the stale override",
			}));
			Assert.That(result.ThirdPartyNotices, Does.Contain("Kept icon\n  License: MIT\n"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("Copyright holders: Icon Author\n\nMIT License"));
		});
	}

	[Test]
	public void Every_asset_file_needs_an_attribution_or_a_first_party_entry()
	{
		using var repository = new FixtureRepository();
		repository.Write("assets/icons/attributed.svg", "<svg/>");
		repository.Write("assets/icons/own.svg", "<svg/>");
		repository.Write("assets/icons/unknown.png", "png");
		repository.Write("assets/icons/readme.md", "not an asset");
		repository.Write("assets/icons/bin/copy.svg", "<svg/>");
		repository.UseAssets("assets/icons", firstParty: "assets/icons/own.svg");
		repository.Attributions("""
			assets:
			  - name: Icon set
			    license: MIT
			    copyright: Icon Author
			    paths: [assets/icons/attributed.svg]
			""");

		var result = repository.Generate();

		Assert.That(result.Problems, Is.EqualTo(new[]
		{
			"asset assets/icons/unknown.png is neither attributed in third-party/attributions.yml nor listed as first-party in third-party/config.yml",
		}));
	}

	[Test]
	public void An_asset_may_be_claimed_only_once()
	{
		using var repository = new FixtureRepository();
		repository.Write("assets/icons/shared.svg", "<svg/>");
		repository.Write("assets/icons/own.svg", "<svg/>");
		repository.UseAssets("assets/icons", firstParty: "assets/icons/own.svg");
		repository.Attributions("""
			assets:
			  - name: First set
			    license: MIT
			    copyright: First Author
			    paths: [assets/icons/shared.svg]
			  - name: Second set
			    license: MIT
			    copyright: Second Author
			    paths: [assets/icons/*.svg]
			""");

		var result = repository.Generate();

		Assert.That(result.Problems, Is.EquivalentTo(new[]
		{
			"asset assets/icons/own.svg is listed as first-party and attributed in third-party/attributions.yml",
			"attributions.yml: assets/icons/shared.svg is claimed by both 'First set' and 'Second set'",
		}));
	}

	[Test]
	public void A_missing_restore_graph_does_not_report_its_overrides_as_stale()
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet(["win-x64"], restoredRuntimeIdentifiers: []);
		repository.Overrides("""
			packages:
			  - ecosystem: nuget
			    name: Needed.Override
			    license: MIT
			""");

		var result = repository.Generate();

		Assert.That(result.Problems, Has.None.Contains("stale override"));
	}
}
