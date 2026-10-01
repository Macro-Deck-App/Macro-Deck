using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Versioning;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A32_UiPreviewClientTests
{
	[Test]
	public async Task A_launched_plugin_lists_its_previews_and_serves_a_tree_for_each()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.LaunchAsync(PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable()));
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var previews = await session.Ui.GetPreviewsAsync();

		Assert.That(previews, Is.Not.Empty);
		Assert.That(previews.Select(preview => preview.Scenario), Does.Contain("Station tile"));

		foreach (var preview in previews)
		{
			var outcome = await session.Ui.OpenPreviewAsync(preview.Id, preview.Profile);

			Assert.That(outcome.Accepted, Is.True, $"{preview.Id}: {outcome.FailureReason}");
			Assert.That(outcome.Tree!.Value.GetProperty("root").GetProperty("id").GetString(), Is.Not.Empty);

			await session.Ui.CloseAsync(outcome.SessionId!);
		}
	}

	[Test]
	public async Task The_widget_scenario_declares_the_widget_profile_and_serves_its_tree()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.LaunchAsync(PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable()));
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var tile = (await session.Ui.GetPreviewsAsync()).Single(preview => preview.Scenario == "Station tile");
		var outcome = await session.Ui.OpenPreviewAsync(tile.Id, tile.Profile);

		Assert.Multiple(() =>
		{
			Assert.That(tile.Profile, Is.EqualTo("widget"));
			Assert.That(outcome.Tree!.Value.GetRawText(), Does.Contain("Primary"));
		});
	}

	[Test]
	public async Task An_unknown_preview_id_is_declined_with_the_plugins_reason()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.LaunchAsync(PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable()));
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));

		var outcome = await session.Ui.OpenPreviewAsync("no-such-preview");

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Accepted, Is.False);
			Assert.That(outcome.SessionId, Is.Null);
			Assert.That(outcome.FailureReason, Does.Contain("no preview"));
		});
	}

	[Test]
	public async Task A_host_that_never_opened_a_preview_still_refuses_the_plugins_ui_pushes()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.LaunchAsync(PluginLaunchSpec.ForExecutable(PluginLocator.FindWellBehavedPluginExecutable()));
		var session = await host.WaitForSessionAsync(TimeSpan.FromSeconds(30));
		var previewId = (await session.Ui.GetPreviewsAsync())[0].Id;
		var sessionId = Guid.NewGuid().ToString();

		var opened = await session.InvokeAsync(CapabilityKinds.Ui,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Ui.SessionOpen,
			new UiSessionOpenArguments
			{
				SessionId = sessionId,
				SurfaceKind = UiSurfaceKinds.DeveloperPreview,
				SessionMode = "exclusive",
				SurfaceAttributes = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["previewId"] = previewId }),
				UiModelVersion = UiModelVersions.Current
			});
		await session.InvokeAsync(CapabilityKinds.Ui,
			ProviderCapabilityId.LocalId,
			CapabilityOperations.Ui.SessionSnapshot,
			new UiSessionSnapshotArguments { SessionId = sessionId });

		await Wait.UntilAsync(() => host.Messages.OfType(MessageTypes.HostResult)
				.Any(message => message.Envelope.Error?.Code == ProtocolErrorCodes.CapabilityUnsupported),
			TimeSpan.FromSeconds(10),
			"the plugin's ui push was never refused");

		Assert.That(opened.Succeeded, Is.True);
	}
}
