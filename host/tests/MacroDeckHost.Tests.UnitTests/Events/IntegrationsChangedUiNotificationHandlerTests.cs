using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Tests.UnitTests.Events;

[TestFixture]
public class IntegrationsChangedUiNotificationHandlerTests
{
	[Test]
	public async Task A_state_change_tells_admin_clients_which_integration_changed()
	{
		var transport = new RecordingUiTransport();
		var handler = new IntegrationsChangedUiNotificationHandler(transport);

		await handler.Handle(new IntegrationStateChangedNotification("com.example.plugin"), CancellationToken.None);

		AssertSingleAdminEvent(transport, "com.example.plugin");
	}

	[Test]
	public async Task A_catalogue_change_tells_admin_clients_which_integration_changed()
	{
		var transport = new RecordingUiTransport();
		var handler = new IntegrationsChangedUiNotificationHandler(transport);

		await handler.Handle(new IntegrationCatalogChangedNotification("com.example.plugin"), CancellationToken.None);

		AssertSingleAdminEvent(transport, "com.example.plugin");
	}

	private static void AssertSingleAdminEvent(RecordingUiTransport transport, string integrationId)
	{
		Assert.That(transport.GroupMessages, Has.Count.EqualTo(1));
		var (group, message) = transport.GroupMessages[0];
		Assert.Multiple(() =>
		{
			Assert.That(group, Is.EqualTo(UiAdminGroups.Admin));
			Assert.That(message, Is.InstanceOf<IntegrationsChangedEvent>());
			Assert.That(((IntegrationsChangedEvent)message).IntegrationId, Is.EqualTo(integrationId));
			Assert.That(transport.Broadcasts, Is.Empty);
		});
	}
}
