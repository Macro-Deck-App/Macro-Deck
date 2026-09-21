using MacroDeck.LicenseTool.Tests.UnitTests.Support;

namespace MacroDeck.LicenseTool.Tests.UnitTests;

public sealed class CargoCollectionTests
{
	private const string Windows = "x86_64-pc-windows-msvc";
	private const string Linux = "x86_64-unknown-linux-gnu";

	private static readonly Dictionary<string, string> DualLicenseFiles = new()
	{
		["LICENSE-MIT"] = "MIT License\n\nCopyright (c) crate authors",
		["LICENSE-APACHE"] = "Apache License\nVersion 2.0, full text",
	};

	[Test]
	public void Only_crates_linked_into_the_binary_are_attributed_with_their_platforms()
	{
		using var repository = new FixtureRepository();
		repository.UseCargo(Windows, Linux);
		foreach (var triple in new[] { Windows, Linux })
		{
			var metadata = new CargoMetadataBuilder(repository);
			var shared = metadata.Crate("shared", "1.0.0", "MIT OR Apache-2.0", DualLicenseFiles);
			var builder = metadata.Crate("build-helper", "1.0.0", "MIT", DualLicenseFiles);
			var derive = metadata.Crate("derive-macro", "1.0.0", "MIT", DualLicenseFiles, procMacro: true);
			var macroDependency = metadata.Crate("macro-only-dependency", "1.0.0", "MIT", DualLicenseFiles);
			metadata.Depend(CargoMetadataBuilder.Root, shared);
			metadata.Depend(CargoMetadataBuilder.Root, builder, kind: "build");
			metadata.Depend(CargoMetadataBuilder.Root, derive);
			metadata.Depend(derive, macroDependency);
			if (triple == Windows)
			{
				var windowsOnly = metadata.Crate("windows-only", "0.5.0", "Apache-2.0 WITH LLVM-exception", DualLicenseFiles);
				metadata.Depend(shared, windowsOnly);
			}

			repository.Cargo.Set(triple, metadata.Build());
		}

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.Problems, Is.Empty);
			Assert.That(result.ThirdPartyNotices, Does.Contain("\nshared\n  License: MIT\n  Declared: MIT OR Apache-2.0\n  URL: https://example.com/shared\n  Texts:"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("\nwindows-only\n  License: Apache-2.0 WITH LLVM-exception\n  URL: https://example.com/windows-only\n  Platforms: Windows\n"));
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain("build-helper"));
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain("derive-macro"));
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain("macro-only-dependency"));
		});
	}

	[Test]
	public void License_files_for_the_license_not_chosen_are_left_out()
	{
		using var repository = new FixtureRepository();
		repository.UseCargo(Linux);
		var metadata = new CargoMetadataBuilder(repository);
		metadata.Depend(CargoMetadataBuilder.Root, metadata.Crate("dual", "1.0.0", "MIT OR Apache-2.0", DualLicenseFiles));
		repository.Cargo.Set(Linux, metadata.Build());

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.ThirdPartyNotices, Does.Contain("Copyright (c) crate authors"));
			Assert.That(result.ThirdPartyNotices, Does.Not.Contain("Version 2.0, full text"));
		});
	}

	[Test]
	public void A_vendored_native_library_is_attributed_through_an_additional_license()
	{
		using var repository = new FixtureRepository();
		repository.UseCargo(Windows);
		var metadata = new CargoMetadataBuilder(repository);
		metadata.Depend(CargoMetadataBuilder.Root, metadata.Crate("loader-sys", "1.0.0", "MIT", new Dictionary<string, string>()));
		repository.Cargo.Set(Windows, metadata.Build());
		repository.Write("third-party/licenses/Vendor.txt", "Vendor SDK license text");
		repository.Overrides("""
			packages:
			  - ecosystem: cargo
			    name: loader-sys
			    additionalLicenses:
			      - title: Vendor loader library
			        license: BSD-3-Clause
			        copyright: Vendor Corporation
			        licenseFile: third-party/licenses/Vendor.txt
			      - title: Copyleft part
			        license: GPL-3.0-only
			""");

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.ThirdPartyNotices, Does.Contain("Vendor loader library\n\nCopyright: Vendor Corporation\n\nVendor SDK license text"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("Copyright holders: loader-sys developers\n\nMIT License"));
			Assert.That(result.Problems, Is.EqualTo(new[]
			{
				"overrides.yml: loader-sys: Copyleft part: license 'GPL-3.0-only' is not allowed by third-party/config.yml; review it and add an override with reviewed: to third-party/overrides.yml, or replace the dependency",
			}));
		});
	}
}
