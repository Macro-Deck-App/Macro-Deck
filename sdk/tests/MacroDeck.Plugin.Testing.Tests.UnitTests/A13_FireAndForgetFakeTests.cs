using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A13 - fire-and-forget fakes never throw; the fakes whose contract reports failure as a return value
/// actually report it.
/// </summary>
[TestFixture]
public class A13_FireAndForgetFakeTests
{
	[Test]
	public void FakeUserNotifier_replaces_under_the_same_key_and_tolerates_an_unknown_dismiss()
	{
		var notifier = new FakeUserNotifier();

		notifier.Notify(new UserNotificationRequest { Title = "first", Key = "connection" });
		notifier.Notify(new UserNotificationRequest { Title = "second", Key = "connection" });

		Assert.Multiple(() =>
		{
			Assert.That(notifier.Current,
				Has.Count.EqualTo(1),
				"the second Notify under the same key must replace, not stack");
			Assert.That(notifier.Current[0].Title, Is.EqualTo("second"));
		});

		Assert.DoesNotThrow(() => notifier.Dismiss("an-unknown-key"));
	}

	[Test]
	public void FakeEventPublisher_Publish_never_throws_regardless_of_parameters()
	{
		var events = new FakeEventPublisher();

		Assert.DoesNotThrow(() => events.Publish("some-event"));
		Assert.DoesNotThrow(() => events.Publish("some-event", new Dictionary<string, object?> { ["value"] = 1 }));
		Assert.DoesNotThrow(() => events.Publish("some-event", new Dictionary<string, object?> { ["value"] = null }));
	}

	[Test]
	public async Task FakeWidgetApi_reports_false_for_unknown_id_and_empty_patch_true_for_a_real_change()
	{
		var widgets = new FakeWidgetApi();
		widgets.Seed(new WidgetTargetInfo
			{ Id = "widget-1", Label = "Widget", Location = "Profile/Folder", Type = "action-button" });

		var forUnknownWidget = await widgets.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "unknown-widget",
			Patch = new WidgetAppearancePatch { Label = "New Label" }
		});

		var forEmptyPatch = await widgets.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "widget-1",
			Patch = new WidgetAppearancePatch()
		});

		var forRealChange = await widgets.ApplyAsync(new WidgetAppearanceRequest
		{
			WidgetId = "widget-1",
			Patch = new WidgetAppearancePatch { Label = "New Label" }
		});

		Assert.Multiple(() =>
		{
			Assert.That(forUnknownWidget, Is.False);
			Assert.That(forEmptyPatch, Is.False);
			Assert.That(forRealChange, Is.True);
			Assert.That(widgets.Exists("unknown-widget"), Is.False);
		});
	}

	[Test]
	public async Task FakeScriptApi_RunAsync_reports_Failed_for_an_unknown_script_id()
	{
		var scripts = new FakeScriptApi();

		var result = await scripts.RunAsync("unknown-script-id");

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task FakeScriptApi_records_the_input_values_a_run_was_given()
	{
		var scripts = new FakeScriptApi();
		scripts.Seed(new Sdk.Scripts.Script { Id = "s1", Name = "Alpha" });

		await scripts.RunAsync("s1",
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["scene"] = "Live", ["volume"] = 42d },
			"c9");

		var run = scripts.Runs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(run.ScriptId, Is.EqualTo("s1"));
			Assert.That(run.OriginClientId, Is.EqualTo("c9"));
			Assert.That(run.Inputs["scene"], Is.EqualTo("Live"));
			Assert.That(run.Inputs["volume"], Is.EqualTo(42d));
		});
	}

	[Test]
	public void A_script_without_declarations_reports_an_empty_input_list_never_null()
	{
		var script = new Sdk.Scripts.Script { Id = "s1", Name = "Alpha" };

		Assert.That(script.Inputs, Is.Not.Null.And.Empty);
	}

	[Test]
	public void FakeActionInteractions_records_returns_synchronously_and_never_throws()
	{
		var interactions = new FakeActionInteractions();

		Assert.DoesNotThrow(() => interactions.RequestItemPicker(null, "instance-1", MusicPlayerCatalogItemKind.Track));
		Assert.DoesNotThrow(() => interactions.RequestDevicePicker(null, "instance-1", startPlayback: true));

		Assert.Multiple(() =>
		{
			Assert.That(interactions.ItemPickerRequests, Has.Count.EqualTo(1));
			Assert.That(interactions.DevicePickerRequests, Has.Count.EqualTo(1));
		});
	}
}
