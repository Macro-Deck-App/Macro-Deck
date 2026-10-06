using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarRegistryTests
{
	[Test]
	public void Accounts_of_every_enabled_provider_are_merged_under_qualified_ids()
	{
		var google = new FakeCalendarIntegration("app.google", "Google Calendar")
			.WithAccount("alice")
			.WithAccount("bob");
		var plugin = new FakeCalendarIntegration("com.example.calendar")
			.WithAccount("work")
			.WithAccount("home");

		var accounts = CalendarTesting.Registry(google, plugin).GetAccounts();

		Assert.Multiple(() =>
		{
			Assert.That(accounts.Select(a => a.AccountId),
				Is.EqualTo(new[]
				{
					"app.google::alice", "app.google::bob", "com.example.calendar::work",
					"com.example.calendar::home"
				}));
			Assert.That(accounts[0].DisplayName, Is.EqualTo("alice@example.com"));
			Assert.That(accounts[0].LocalAccountId, Is.EqualTo("alice"));
			Assert.That(TestLocalization.Resolve(accounts[0].ProviderName), Is.EqualTo("Google Calendar"));
			Assert.That(TestLocalization.Resolve(accounts[2].ProviderName),
				Is.EqualTo("com.example.calendar integration"),
				"a provider without a name of its own is described by its integration");
		});
	}

	[Test]
	public void Invalid_and_duplicate_account_ids_are_dropped()
	{
		var provider = new FakeCalendarIntegration("app.google").WithAccount("alice");
		provider.Accounts.Add(new CalendarAccount { Id = "alice", DisplayName = "Alice again" });
		provider.Accounts.Add(new CalendarAccount { Id = "has space", DisplayName = "Broken" });
		provider.Accounts.Add(new CalendarAccount { Id = "x::y", DisplayName = "Smuggled" });
		provider.Accounts.Add(new CalendarAccount { Id = string.Empty, DisplayName = "Empty" });

		var accounts = CalendarTesting.Registry(provider).GetAccounts();

		Assert.That(accounts.Select(a => (a.AccountId, a.DisplayName)),
			Is.EqualTo(new[] { ("app.google::alice", "alice@example.com") }));
	}

	[Test]
	public void A_disabled_provider_contributes_no_accounts()
	{
		var provider = new FakeCalendarIntegration("app.google").WithAccount("alice");
		var registry = new MacroDeckHost.Application.Calendar.CalendarRegistry(
			new ConfigurableIntegrationRegistry([provider], disabled: ["app.google"]),
			CalendarTesting.Logger());

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetAccounts(), Is.Empty);
			Assert.That(registry.Resolve("app.google::alice"), Is.Null);
		});
	}
}
