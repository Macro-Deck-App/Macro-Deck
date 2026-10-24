using MacroDeckHost.Integrations.AutoHotkey.Protocol;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class AhkPreludeTests
{
	[TestCase("plain", "\"plain\"")]
	[TestCase("a`b", "\"a``b\"")]
	[TestCase("say \"hi\"", "\"say `\"hi`\"\"")]
	[TestCase("a ;comment", "\"a `;comment\"")]
	[TestCase("line1\r\nline2\tend", "\"line1`r`nline2`tend\"")]
	[TestCase("{braces} %percent% 100%", "\"{braces} %percent% 100%\"")]
	public void A_value_becomes_a_quoted_literal_that_cannot_end_early(string value, string expected)
	{
		Assert.That(AhkPrelude.Literal(value), Is.EqualTo(expected));
	}

	[Test]
	public void A_value_with_a_nul_character_is_rejected()
	{
		Assert.Throws<AhkPreludeException>(() => AhkPrelude.Literal("a\0b"));
	}

	[TestCase("1abc")]
	[TestCase("has space")]
	[TestCase("a-b")]
	[TestCase("\"quote")]
	[TestCase("")]
	public void A_parameter_name_that_is_no_identifier_is_rejected(string key)
	{
		var exception = Assert.Throws<AhkPreludeException>(() => AhkPrelude.Build(
			new Dictionary<string, string> { [key] = "value" },
			true));

		Assert.That(exception!.Key, Is.EqualTo(key));
	}

	[Test]
	public void The_prelude_assigns_every_parameter_before_announcing_readiness()
	{
		var prelude = AhkPrelude.Build(new Dictionary<string, string> { ["user_name"] = "Ann \"A\"", ["_x2"] = "" },
			true);

		var assignment = prelude.IndexOf("MacroDeck.Params[\"user_name\"] := \"Ann `\"A`\"\"",
			StringComparison.Ordinal);
		var empty = prelude.IndexOf("MacroDeck.Params[\"_x2\"] := \"\"", StringComparison.Ordinal);
		var ready = prelude.IndexOf("MacroDeck._Ready()", StringComparison.Ordinal);
		Assert.Multiple(() =>
		{
			Assert.That(assignment, Is.GreaterThan(0));
			Assert.That(empty, Is.GreaterThan(0));
			Assert.That(ready, Is.GreaterThan(Math.Max(assignment, empty)));
		});
	}

	[Test]
	public void Only_the_helper_prelude_carries_the_helper_calls()
	{
		var withHelper = AhkPrelude.Build(new Dictionary<string, string>(), true);
		var withoutHelper = AhkPrelude.Build(new Dictionary<string, string>(), false);

		Assert.Multiple(() =>
		{
			Assert.That(withHelper, Does.Contain("static GetVar(").And.Contain("static IsAvailable := true"));
			Assert.That(withoutHelper, Does.Not.Contain("static GetVar(").And.Contain("static IsAvailable := false"));
			Assert.That(withoutHelper, Does.Contain("static Params := Map()"));
		});
	}
}
