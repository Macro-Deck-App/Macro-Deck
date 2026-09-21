using MacroDeck.LicenseTool.Tests.UnitTests.Support;

namespace MacroDeck.LicenseTool.Tests.UnitTests;

public sealed class NpmCollectionTests
{
	private const string Workspaces = """
		    - path: web
		      tsconfig: tsconfig.json
		      entries:
		        - src/main.ts
		      styleRoots:
		        - src
		""";

	private static FixtureRepository WebWorkspace()
	{
		var repository = new FixtureRepository();
		repository.UseNpm(Workspaces);
		repository.Write("ui/web/tsconfig.json", """
			/* comments are allowed here */
			{
			  "compilerOptions": {
			    "paths": { "@shared": ["./src/shared/index.ts"] },
			  },
			}
			""");
		return repository;
	}

	[Test]
	public void Only_code_reachable_from_the_shipped_entries_counts()
	{
		using var repository = WebWorkspace();
		repository.NpmWorkspace("web", "declared-dependency", "@types/declared");
		repository.Write("ui/web/src/main.ts", """
			import { helper } from './helper';
			import { shared } from '@shared';
			import polyfill from 'dev-polyfill/auto';
			""");
		repository.Write("ui/web/src/helper.ts", "export { lodash } from 'lib-from-helper';");
		repository.Write("ui/web/src/shared/index.ts", "export * from 'lib-from-alias';");
		repository.Write("ui/web/src/build.mjs", "import esbuild from 'esbuild';");
		repository.Write("ui/web/src/main.test.mjs", "import { JSDOM } from 'jsdom';");
		repository.NpmPackage("node_modules/declared-dependency", "1.0.0", "MIT");
		repository.NpmPackage("node_modules/@types/declared", "1.0.0", "MIT");
		repository.NpmPackage("node_modules/dev-polyfill", "1.0.0", "MIT", ["polyfill-helper"], dev: true);
		repository.NpmPackage("node_modules/polyfill-helper", "1.0.0", "ISC", dev: true);
		repository.NpmPackage("node_modules/lib-from-helper", "1.0.0", "MIT");
		repository.NpmPackage("node_modules/lib-from-alias", "1.0.0", "MIT");
		repository.NpmPackage("node_modules/esbuild", "1.0.0", "MIT", dev: true);
		repository.NpmPackage("node_modules/jsdom", "1.0.0", "MIT", dev: true);

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.Problems, Is.Empty);
			foreach (var shipped in new[] { "declared-dependency", "dev-polyfill", "polyfill-helper", "lib-from-helper", "lib-from-alias" })
			{
				Assert.That(result.ThirdPartyNotices, Does.Contain($"\n{shipped}\n  License:"), shipped);
			}

			foreach (var notShipped in new[] { "@types/declared", "esbuild", "jsdom" })
			{
				Assert.That(result.ThirdPartyNotices, Does.Not.Contain($"\n{notShipped}\n"), notShipped);
			}
		});
	}

	[Test]
	public void Nested_versions_resolve_to_the_nearest_install_and_merge_into_one_entry()
	{
		using var repository = WebWorkspace();
		repository.NpmWorkspace("web", "outer", "inner");
		repository.Write("ui/web/src/main.ts", "");
		repository.NpmPackage("node_modules/outer", "1.0.0", "MIT", ["inner"]);
		repository.NpmPackage("node_modules/outer/node_modules/inner", "2.0.0", "ISC", licenseText: "inner 2 license");
		repository.NpmPackage("node_modules/inner", "1.0.0", "MIT", licenseText: "inner 1 license");

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.ThirdPartyNotices, Does.Contain("\ninner\n  License: ISC; MIT\n"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("inner 1 license"));
			Assert.That(result.ThirdPartyNotices, Does.Contain("inner 2 license"));
		});
	}

	[Test]
	public void Platform_restricted_packages_never_count_whether_or_not_they_are_installed()
	{
		string Generate(bool installed)
		{
			using var repository = WebWorkspace();
			repository.NpmWorkspace("web", "tool");
			repository.Write("ui/web/src/main.ts", "");
			repository.NpmPackage("node_modules/tool", "1.0.0", "MIT", ["tool-darwin"]);
			repository.NpmPackage("node_modules/tool-darwin", "1.0.0", "MIT", osRestricted: true, install: installed);
			var result = repository.Generate();
			Assert.That(result.Problems, Is.Empty);
			return result.ThirdPartyNotices;
		}

		var withInstall = Generate(installed: true);

		Assert.Multiple(() =>
		{
			Assert.That(withInstall, Does.Not.Contain("tool-darwin"));
			Assert.That(withInstall, Is.EqualTo(Generate(installed: false)));
		});
	}

	[Test]
	public void Stylesheet_loads_resolve_locally_before_npm()
	{
		using var repository = WebWorkspace();
		repository.NpmWorkspace("web");
		repository.Write("ui/web/src/main.ts", "");
		repository.Write("ui/web/src/styles/main.scss", """
			// @use './commented/out';
			@use 'sass:math';
			@use 'variables' as vars;
			@use 'components/forms';
			@use '../theme';
			@use 'style-package/dist/base';
			""");
		repository.Write("ui/web/src/styles/_variables.scss", "");
		repository.Write("ui/web/src/styles/components/_forms.scss", "@use '../variables';");
		repository.Write("ui/web/src/theme.css", "");
		repository.NpmPackage("node_modules/style-package", "1.0.0", "MIT");

		var result = repository.Generate();

		Assert.Multiple(() =>
		{
			Assert.That(result.Problems, Is.Empty);
			Assert.That(result.ThirdPartyNotices, Does.Contain("\nstyle-package\n  License: MIT"));
		});
	}

	[Test]
	public void Unresolvable_imports_fail_with_their_location()
	{
		using var repository = WebWorkspace();
		repository.NpmWorkspace("web");
		repository.Write("ui/web/src/main.ts", "import './missing';\nimport 'not-installed';");
		repository.Write("ui/web/src/broken.scss", "@use '../nowhere';");

		var result = repository.Generate();

		Assert.That(result.Problems, Is.EquivalentTo(new[]
		{
			"npm: relative stylesheet load not found: ui/web/src/broken.scss: ../nowhere",
			"npm: ui/web/src/main.ts: import './missing' does not resolve to a file",
			"npm: ui/web/src/main.ts: package 'not-installed' is not in package-lock.json",
		}));
	}
}
