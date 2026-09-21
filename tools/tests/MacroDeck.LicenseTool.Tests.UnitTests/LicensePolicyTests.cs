using MacroDeck.LicenseTool.Licensing;

namespace MacroDeck.LicenseTool.Tests.UnitTests;

public sealed class LicensePolicyTests
{
	private static readonly LicensePolicy Policy = new(["MIT", "ISC", "Apache-2.0"], ["LLVM-exception"]);

	[TestCase("MIT OR Apache-2.0")]
	[TestCase("Apache-2.0 OR MIT")]
	[TestCase("(Apache-2.0 OR MIT)")]
	[TestCase("Apache-2.0 / MIT")]
	[TestCase("MIT/Apache-2.0")]
	public void A_choice_is_attributed_under_the_preferred_allowed_license(string expression)
	{
		var selected = Policy.Select(expression, []);

		Assert.That(selected!.Select(license => license.ToString()), Is.EqualTo(new[] { "MIT" }));
	}

	[Test]
	public void A_choice_falls_back_to_the_allowed_branch_when_the_other_is_not_allowed()
	{
		var selected = Policy.Select("GPL-3.0-only OR ISC", []);

		Assert.That(selected!.Select(license => license.ToString()), Is.EqualTo(new[] { "ISC" }));
	}

	[Test]
	public void A_conjunction_needs_every_license_allowed()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Policy.Select("Apache-2.0 AND ISC", [])!.Select(license => license.ToString()),
				Is.EqualTo(new[] { "Apache-2.0", "ISC" }));
			Assert.That(Policy.Select("MIT AND GPL-3.0-only", []), Is.Null);
		});
	}

	[Test]
	public void A_custom_license_is_only_accepted_once_reviewed()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Policy.Select("LicenseRef-Custom", []), Is.Null);
			Assert.That(Policy.Select("LicenseRef-Custom", ["LicenseRef-Custom"])!.Single().Id, Is.EqualTo("LicenseRef-Custom"));
		});
	}

	[Test]
	public void An_exception_must_be_allowed_as_well()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Policy.Select("Apache-2.0 WITH LLVM-exception", [])!.Single().ToString(),
				Is.EqualTo("Apache-2.0 WITH LLVM-exception"));
			Assert.That(Policy.Select("Apache-2.0 WITH Classpath-exception-2.0", []), Is.Null);
		});
	}

	[TestCase("MIT OR")]
	[TestCase("(MIT")]
	[TestCase("AND MIT")]
	public void A_malformed_expression_is_reported(string expression) =>
		Assert.That(() => Policy.Select(expression, []), Throws.TypeOf<FormatException>());
}
