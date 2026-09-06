using MacroDeckHost.Integrations.Http.Actions;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpStatusExpectationTests
{
	[TestCase(200, true)]
	[TestCase(204, true)]
	[TestCase(299, true)]
	[TestCase(199, false)]
	[TestCase(300, false)]
	public void A_class_wildcard_matches_only_its_own_hundred_block(int statusCode, bool expected)
	{
		Assert.That(HttpStatusExpectation.TryParse("2xx", out var expectation), Is.True);
		Assert.That(expectation.Matches(statusCode), Is.EqualTo(expected));
	}

	[TestCase(200, true)]
	[TestCase(201, true)]
	[TestCase(404, true)]
	[TestCase(202, false)]
	[TestCase(500, false)]
	public void An_exact_list_matches_only_the_listed_codes(int statusCode, bool expected)
	{
		Assert.That(HttpStatusExpectation.TryParse("200,201,404", out var expectation), Is.True);
		Assert.That(expectation.Matches(statusCode), Is.EqualTo(expected));
	}

	[TestCase(200, true)]
	[TestCase(202, true)]
	[TestCase(204, true)]
	[TestCase(199, false)]
	[TestCase(205, false)]
	public void An_inclusive_range_matches_both_its_endpoints(int statusCode, bool expected)
	{
		Assert.That(HttpStatusExpectation.TryParse("200-204", out var expectation), Is.True);
		Assert.That(expectation.Matches(statusCode), Is.EqualTo(expected));
	}

	[TestCase("*")]
	[TestCase("any")]
	[TestCase("ANY")]
	public void A_wildcard_matches_anything(string expression)
	{
		Assert.That(HttpStatusExpectation.TryParse(expression, out var expectation), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(expectation.Matches(100), Is.True);
			Assert.That(expectation.Matches(200), Is.True);
			Assert.That(expectation.Matches(599), Is.True);
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void A_blank_expression_falls_back_to_2xx(string? expression)
	{
		Assert.That(HttpStatusExpectation.TryParse(expression, out var expectation), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(expectation.Matches(200), Is.True);
			Assert.That(expectation.Matches(300), Is.False);
		});
	}

	[TestCase("abc")]
	[TestCase("2yy")]
	[TestCase("204-200")]
	[TestCase("700")]
	[TestCase("99")]
	[TestCase("200-")]
	[TestCase("-200")]
	public void An_unparseable_expression_fails(string expression)
	{
		Assert.That(HttpStatusExpectation.TryParse(expression, out _), Is.False);
	}
}
