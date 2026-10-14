using System.Text.Json;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Notifications;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>A34 - a plugin that asks a question through the notification service can be answered in a test.</summary>
[TestFixture]
public class A34NotificationServiceFakeTests
{
	[Test]
	public async Task A_test_answers_a_notification_and_the_plugin_gets_the_result_once()
	{
		var service = new FakeNotificationService();
		var asking = service.ShowForResultAsync(new NotificationOptions
			{ Title = "Delete?", Popup = NotificationPopup.Dialog });

		var notification = service.Open.Single();
		var first = notification.Respond("delete",
			new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement("old") });
		var second = notification.Respond("keep");
		var result = await asking;

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.True);
			Assert.That(second, Is.False);
			Assert.That(result.CloseReason, Is.EqualTo(NotificationCloseReason.Action));
			Assert.That(result.ActionId, Is.EqualTo("delete"));
			Assert.That(result.Values["reason"].GetString(), Is.EqualTo("old"));
			Assert.That(service.Open, Is.Empty);
		});
	}

	[Test]
	public async Task Cancelling_the_wait_dismisses_the_notification()
	{
		var service = new FakeNotificationService();
		using var cancellation = new CancellationTokenSource();
		var asking = service.ShowForResultAsync(new NotificationOptions { Title = "Q" }, cancellation.Token);

		await cancellation.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await asking);
		Assert.That(service.Shown.Single().Completion.Result.CloseReason,
			Is.EqualTo(NotificationCloseReason.Programmatic));
	}

	[Test]
	public async Task Updates_are_recorded_and_the_context_exposes_the_fake()
	{
		var context = new FakeIntegrationContext();
		var handle = await ((Sdk.IIntegrationContext)context).NotificationService.ShowAsync(new NotificationOptions
			{ Title = "Sync" });

		await handle.UpdateAsync(new NotificationUpdate { Progress = new NotificationProgress(1, 2) });

		Assert.That(context.NotificationService.Shown.Single().Updates.Single().Progress,
			Is.EqualTo(new NotificationProgress(1, 2)));
	}
}
