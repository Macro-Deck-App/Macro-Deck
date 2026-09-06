using MacroDeck.Ui.Model.Identity;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Identity;

[TestFixture]
public class UiIdentifierTests
{
	[TestCase("a")]
	[TestCase("A")]
	[TestCase("9")]
	[TestCase("9lead")]
	[TestCase("a.b-c_d")]
	[TestCase("a1.2-3_4")]
	public void IsValid_accepts_the_documented_grammar(string value)
		=> Assert.That(UiIdentifier.IsValid(value), Is.True);

	[Test]
	public void IsValid_accepts_128_characters()
		=> Assert.That(UiIdentifier.IsValid(new string('a', 128)), Is.True);

	[TestCase(null)]
	[TestCase("")]
	[TestCase(" ")]
	[TestCase("a b")]
	[TestCase(".lead")]
	[TestCase("-lead")]
	[TestCase("_lead")]
	[TestCase("a/b")]
	[TestCase("a\n")]
	public void IsValid_rejects_everything_outside_the_grammar(string? value)
		=> Assert.DoesNotThrow(() => Assert.That(UiIdentifier.IsValid(value), Is.False));

	[Test]
	public void IsValid_rejects_129_characters() => Assert.That(UiIdentifier.IsValid(new string('a', 129)), Is.False);

	[TestCase("a:b")]
	[TestCase("integrationId::localId")]
	[TestCase(":")]
	[TestCase("a:")]
	public void IsValid_rejects_the_plugin_id_separator(string value)
		=> Assert.That(UiIdentifier.IsValid(value), Is.False);

	[TestCase(null)]
	[TestCase("")]
	[TestCase("a b")]
	[TestCase("a")]
	[TestCase("item.3")]
	[TestCase("field-Name_2")]
	public void TryValidate_reports_an_error_exactly_when_the_value_is_invalid(string? value)
	{
		var isValid = UiIdentifier.IsValid(value);
		var result = UiIdentifier.TryValidate(value, out var error);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(isValid));
			Assert.That(error is null, Is.EqualTo(isValid));
			if (error is not null)
			{
				Assert.That(error, Is.Not.Empty);
			}
		});
	}

	[TestCase(127, ExpectedResult = true)]
	[TestCase(128, ExpectedResult = true)]
	[TestCase(129, ExpectedResult = false)]
	public bool Length_boundary_is_at_the_documented_maximum(int length)
		=> UiIdentifier.IsValid(new string('a', length));
}
