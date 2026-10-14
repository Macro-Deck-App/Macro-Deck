using System.Globalization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Locale;
using MacroDeck.Sdk.Storage;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A37StorageLocaleAndHttpFakeTests
{
	[Test]
	public async Task A_seeded_damaged_key_reads_as_unreadable_until_it_is_written()
	{
		var storage = new FakeIntegrationStorage();
		storage.SetUnreadable("state.json");

		var before = await storage.ReadAsync("state.json");
		await storage.WriteAsync("state.json", "{}");
		var after = await storage.ReadAsync("state.json");

		Assert.Multiple(() =>
		{
			Assert.That(before.Status, Is.EqualTo(IntegrationStorageReadStatus.Unreadable));
			Assert.That(after.Content, Is.EqualTo("{}"));
		});
	}

	[Test]
	public async Task A_copy_the_validator_refuses_reads_as_unreadable()
	{
		var storage = new FakeIntegrationStorage();
		storage.Set("state.json", "not json");

		var read = await storage.ReadAsync("state.json", content => content.StartsWith('{'));

		Assert.That(read.Status, Is.EqualTo(IntegrationStorageReadStatus.Unreadable));
	}

	[Test]
	public void The_fake_refuses_the_keys_macro_deck_refuses()
	{
		Assert.ThrowsAsync<ArgumentException>(() => new FakeIntegrationStorage().WriteAsync("../escape", "{}"));
	}

	[Test]
	public async Task An_integration_under_the_harness_uses_the_contexts_storage_locale_and_user_agent()
	{
		var integration = new RecordingIntegration();
		await using var harness = PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => integration));
		harness.Context.Storage.Set("greeting.txt", "hello");
		harness.Context.Locale.TimeOfDayFormat
			= new TimeOfDayFormat(CultureInfo.GetCultureInfo("en-US"), HourCycles.H12);
		harness.Context.Http.UserAgent = "Test/1.0";

		await harness.InitializeIntegrationsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Greeting, Is.EqualTo("hello"));
			Assert.That(integration.Time, Does.Match(@"^3:05\sPM$"));
			Assert.That(integration.UserAgent, Is.EqualTo("Test/1.0"));
			Assert.That(harness.Context.Storage.Get("seen.txt"), Is.EqualTo("yes"));
		});
	}

	private sealed class RecordingIntegration : IPluginIntegration
	{
		public string? Greeting { get; private set; }

		public string? Time { get; private set; }

		public string? UserAgent { get; private set; }

		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
		{
			Greeting = (await context.Storage.ReadAsync("greeting.txt")).Content;
			await context.Storage.WriteAsync("seen.txt", "yes");
			Time = (await context.Locale.GetTimeOfDayFormatAsync()).Format(new TimeOnly(15, 5));
			UserAgent = context.Http.UserAgent;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}
}
