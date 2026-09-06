using MacroDeckHost.Integrations.Http.Client;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpSharedClientsTests
{
	[Test]
	public void The_same_settings_pair_answers_with_the_same_client_instance()
	{
		var first = HttpSharedClients.Get(followRedirects: true, validateTls: true);
		var second = HttpSharedClients.Get(followRedirects: true, validateTls: true);

		Assert.That(second, Is.SameAs(first));
	}

	[Test]
	public void Each_of_the_four_settings_pairs_answers_with_a_distinct_client_instance()
	{
		var clients = new[]
		{
			HttpSharedClients.Get(true, true),
			HttpSharedClients.Get(true, false),
			HttpSharedClients.Get(false, true),
			HttpSharedClients.Get(false, false)
		};

		Assert.That(clients.Distinct().Count(), Is.EqualTo(4));
	}
}
