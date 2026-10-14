using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.SystemIntegrations.Email;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailIntegrationTests
{
	[Test]
	public async Task
		One_email_inbox_widget_type_is_registered_under_its_frozen_id_and_starts_as_an_unread_counter_for_every_inbox()
	{
		var registry = TestWidgetTypeProviders.Registry();
		var host = new WidgetTypeProviderHost(registry, TimeProvider.System, Serilog.Core.Logger.None);

		await host.StartAsync(new EmailIntegration());

		Assert.That(registry.TryResolve("app.macro-deck.email::inbox", out var inbox), Is.True);
		using var defaults = JsonDocument.Parse(inbox.Descriptor.DefaultData!);
		Assert.Multiple(() =>
		{
			Assert.That(registry.All.Where(entry => entry.ProviderId == inbox.ProviderId)
					.Select(entry => entry.WidgetTypeId),
				Is.EqualTo(new[] { "app.macro-deck.email::inbox" }));
			Assert.That(inbox.Descriptor.HasConfiguration, Is.True);
			Assert.That(inbox.Descriptor.SupportsFlows, Is.True);
			Assert.That(defaults.RootElement.GetProperty("mode").GetString(), Is.EqualTo("count"));
			Assert.That(defaults.RootElement.GetProperty("inboxes").GetArrayLength(),
				Is.Zero,
				"no selection means every inbox");
			Assert.That(defaults.RootElement.GetProperty("maxMessages").GetInt32(), Is.EqualTo(5));
		});
	}

	[Test]
	public async Task Refresh_email_inboxes_asks_for_every_inbox_to_be_read_again()
	{
		var imap = new FakeEmailIntegration("app.imap").WithInbox("work");
		var integrations = new ConfigurableIntegrationRegistry([imap]);
		using var world = new EmailWorld(integrations);
		var integration = new EmailIntegration();
		integration.UseEmailServices(new EmailHostServices(world.Cache, world.Keys, integrations));

		var result = await Execute(integration);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(world.Signal.TakePending().All, Is.True);
		});
	}

	[Test]
	public async Task Refresh_email_inboxes_fails_with_a_message_before_the_host_services_are_bound()
	{
		var result = await Execute(new EmailIntegration());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Empty);
		});
	}

	private static Task<ActionResult> Execute(EmailIntegration integration)
	{
		return integration.Actions.Single(action => action.Id == "refresh-inboxes")
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });
	}
}
