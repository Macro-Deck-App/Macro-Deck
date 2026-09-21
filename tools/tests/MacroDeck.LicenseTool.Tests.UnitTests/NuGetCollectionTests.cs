using MacroDeck.LicenseTool.Tests.UnitTests.Support;

namespace MacroDeck.LicenseTool.Tests.UnitTests;

public sealed class NuGetCollectionTests
{
	private static readonly Dictionary<string, string[]> Managed = new() { ["*"] = [] };

	[Test]
	public void Only_packages_with_assets_in_the_published_output_are_attributed()
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet("win-x64", "osx-arm64");
		repository.NuGetPackage("Meta.Package", "1.0.0", "MIT", Managed);
		repository.NuGetPackage("Native.Mac", "2.0.0", "MIT",
			new Dictionary<string, string[]> { ["osx-arm64"] = ["runtimes/osx/native/libnative.dylib"] });
		repository.NuGetPackage("Native.Everywhere", "3.0.0", "ISC",
			new Dictionary<string, string[]> { ["*"] = ["runtimes/any/native/lib.so"] });

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.Problems, Is.Empty);
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain("Meta.Package"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("Native.Mac\n  License: MIT\n  URL: https://example.com/Native.Mac\n  Platforms: macOS\n"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("Native.Everywhere\n  License: ISC\n  URL: https://example.com/Native.Everywhere\n  Texts:"));
		});
	}

	[Test]
	public void A_missing_runtime_graph_asks_for_the_restore_that_produces_it()
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet(["win-x64", "linux-x64"], restoredRuntimeIdentifiers: ["win-x64"]);
		repository.NuGetPackage("Native.Win", "1.0.0", "MIT", new Dictionary<string, string[]> { ["*"] = ["a.dll"] });

		var result = repository.Generate();

		Assert.That(result.Problems, Has.Some.Contains("dotnet restore host/App/App.csproj '-p:RuntimeIdentifiers=\"win-x64;linux-x64\"'"));
	}

	[Test]
	public void A_package_licensed_only_by_file_needs_an_override()
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet("win-x64");
		repository.NuGetPackage("File.Licensed", "1.0.0", null, new Dictionary<string, string[]> { ["*"] = ["a.dll"] },
			new Dictionary<string, string> { ["EULA.txt"] = "Custom terms" }, licenseFile: "EULA.txt");

		var result = repository.Generate();

		Assert.That(result.Problems, Has.Some.Contains("nuget File.Licensed: no license metadata"));
	}

	[Test]
	public void Package_license_files_are_reproduced_and_the_standard_text_used_otherwise()
	{
		using var repository = new FixtureRepository();
		repository.UseNuGet("win-x64");
		repository.NuGetPackage("With.File", "1.0.0", "MIT", new Dictionary<string, string[]> { ["*"] = ["a.dll"] },
			new Dictionary<string, string> { ["LICENSE.txt"] = "MIT License\n\nCopyright (c) With File authors" });
		repository.NuGetPackage("Without.File", "1.0.0", "MIT", new Dictionary<string, string[]> { ["*"] = ["b.dll"] });

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.ThirdPartyNotices, Does.Contain("Copyright (c) With File authors"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("Copyright holders: Without.File Authors\n\nMIT License\n\nStandard MIT text."));
		});
	}
}
