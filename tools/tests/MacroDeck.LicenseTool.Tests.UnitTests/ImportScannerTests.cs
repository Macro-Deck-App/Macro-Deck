using MacroDeck.LicenseTool.Npm;

namespace MacroDeck.LicenseTool.Tests.UnitTests;

public sealed class ImportScannerTests
{
	[Test]
	public void Every_module_import_form_is_found()
	{
		const string source = """
			import 'side-effect';
			import def from "default-import";
			import def2, { named } from 'mixed/sub/path';
			import * as all from '@scope/pkg/deep';
			import {
				multi,
				line,
			} from 'multi-line';
			export * from 'star-export';
			export { reexported } from 'named-export';
			const lazy = () => import('./lazy');
			const legacy = require('required');
			""";

		Assert.That(ScriptImportScanner.Scan(source), Is.EqualTo(new[]
		{
			"side-effect", "default-import", "mixed/sub/path", "@scope/pkg/deep", "multi-line", "star-export",
			"named-export", "./lazy", "required",
		}));
	}

	[Test]
	public void Look_alikes_in_strings_comments_regexes_and_members_are_ignored()
	{
		const string source = """
			import type { Shape } from 'types-only';
			export type { Other } from 'types-only-too';
			// import 'commented-out';
			/* export * from 'block-comment'; */
			const message = 'Macro Deck UI is stale: built from ' + commit;
			const template = `import x from 'in-template' ${value}`;
			const quote = /['"]from 'regex'/g;
			this.iconPacks.import(packId, files);
			const url = new URL('./worker.js', import.meta.url);
			export const value = 1;
			import 'real';
			""";

		Assert.That(ScriptImportScanner.Scan(source), Is.EqualTo(new[] { "real" }));
	}

	[Test]
	public void Stylesheet_loads_skip_comments_and_module_configuration()
	{
		const string source = """
			// @use 'commented-out';
			/* @import 'block-commented'; */
			@use 'sass:math';
			@use 'variables' as vars;
			@use 'theme' with ($accent: 'not-a-target');
			@forward 'components/forms';
			@import 'first', 'second';
			@import url('https://example.com/font.css');
			.icon { content: '@use "not-a-rule"'; }
			""";

		Assert.That(StylesheetImportScanner.Scan(source, lineComments: true), Is.EqualTo(new[]
		{
			"sass:math", "variables", "theme", "components/forms", "first", "second",
		}));
	}
}
