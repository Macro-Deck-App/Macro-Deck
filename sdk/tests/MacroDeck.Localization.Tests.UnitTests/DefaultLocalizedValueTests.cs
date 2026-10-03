using System.Text.Json;

namespace MacroDeck.Localization.Tests.UnitTests;

[TestFixture]
public class DefaultLocalizedValueTests
{
	[Test]
	public void A_default_localized_string_has_no_arguments_instead_of_throwing()
	{
		var value = default(LocalizedString);

		Assert.Multiple(() =>
		{
			Assert.That(value.Arguments, Is.Not.Null.And.Empty);
			Assert.That(() => value.Equals(default(LocalizedString)), Throws.Nothing);
			Assert.That(() => value.GetHashCode(), Throws.Nothing);
			Assert.That(() => value.ToString(), Throws.Nothing);
		});
	}

	[Test]
	public void A_default_localized_string_converts_to_absent_text()
	{
		LocalizedText text = default(LocalizedString);

		Assert.That(text.IsEmpty, Is.True);
	}

	[Test]
	public void Absent_text_from_a_default_reference_serializes_as_null_and_reads_back_absent()
	{
		LocalizedText text = default(LocalizedString);

		var json = JsonSerializer.Serialize(text);
		var read = JsonSerializer.Deserialize<LocalizedText>(json);

		Assert.Multiple(() =>
		{
			Assert.That(json, Is.EqualTo("null"));
			Assert.That(read.IsEmpty, Is.True);
		});
	}

	[Test]
	public void A_real_reference_still_serializes_as_a_localized_object()
	{
		LocalizedText text = new LocalizedString(LocalizationKey.MacroDeck("Common.Save"));

		Assert.That(JsonSerializer.Serialize(text), Does.Contain("\"$localized\""));
	}
}
