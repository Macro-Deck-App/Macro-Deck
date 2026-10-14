using MacroDeck.Sdk.Email;

namespace MacroDeck.Sdk.Tests.UnitTests.Email;

[TestFixture]
public class EmailMessageQueryTests
{
	[Test]
	public void A_query_asks_for_the_ten_newest_messages_of_any_read_state_by_default()
	{
		var query = new EmailMessageQuery();

		Assert.Multiple(() =>
		{
			Assert.That(query.MaxMessages, Is.EqualTo(10));
			Assert.That(query.UnreadOnly, Is.False);
		});
	}

	[TestCase(1)]
	[TestCase(EmailMessageQuery.MaxMessagesLimit)]
	public void A_provider_is_asked_for_between_one_and_fifty_messages(int maxMessages)
	{
		Assert.That(new EmailMessageQuery { MaxMessages = maxMessages }.MaxMessages, Is.EqualTo(maxMessages));
	}

	[TestCase(0)]
	[TestCase(-1)]
	[TestCase(EmailMessageQuery.MaxMessagesLimit + 1)]
	public void A_query_outside_that_range_cannot_be_built(int maxMessages)
	{
		Assert.That(() => new EmailMessageQuery { MaxMessages = maxMessages },
			Throws.TypeOf<ArgumentOutOfRangeException>());
	}
}
