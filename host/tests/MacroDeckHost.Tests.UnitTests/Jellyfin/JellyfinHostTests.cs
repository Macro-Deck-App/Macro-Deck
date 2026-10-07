using System.Text.Json;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Jellyfin;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.Jellyfin;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed class JellyfinConfigurationMutationAdapterTests
{
	[Test]
	public void Saving_an_edit_keeps_devices_learned_while_the_dialog_was_open_and_the_variable_key()
	{
		var adapter = new JellyfinConfigurationMutationAdapter(new ConfigurableIntegrationRegistry([]),
			new VariableRegistry(),
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			() => 1234);
		var entryId = Guid.NewGuid();
		var stored = new ConfigEntryRecord(entryId,
			JellyfinIntegration.IntegrationId,
			"Home",
			DateTime.UtcNow,
			new Dictionary<string, JsonElement>
			{
				[JellyfinIntegration.VariableKeyConfigKey] = JsonSerializer.SerializeToElement("home"),
				[JellyfinConfigKeys.Devices] = JsonSerializer.SerializeToElement("[\"current\"]")
			});
		var submitted = new Dictionary<string, JsonElement>
		{
			[JellyfinConfigKeys.Devices] = JsonSerializer.SerializeToElement("[\"stale\"]")
		};

		var result = adapter.Prepare(new IntegrationConfigMutationPreparation(JellyfinIntegration.IntegrationId,
			entryId,
			"Home",
			submitted,
			stored,
			[],
			TitleChanged: false));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Values[JellyfinConfigKeys.Devices].GetString(), Is.EqualTo("[\"current\"]"));
			Assert.That(result.Values[JellyfinIntegration.VariableKeyConfigKey].GetString(), Is.EqualTo("home"));
		});
	}
}

[TestFixture]
internal sealed class JellyfinSessionsWidgetStateTests
{
	[Test]
	public void Without_a_server_the_widget_asks_for_one()
		=> Assert.That(JellyfinSessionsState.Compute([], null).Status, Is.EqualTo(JellyfinSessionsStatus.NoServer));

	[Test]
	public void An_unreachable_server_is_not_shown_as_idle()
	{
		var state = JellyfinSessionsState.Compute([new JellyfinServerSummary("a", "Home", false, [])], null);

		Assert.That(state.Status, Is.EqualTo(JellyfinSessionsStatus.NotConnected));
	}

	[Test]
	public void Playing_sessions_come_before_paused_ones_and_a_server_filter_applies()
	{
		JellyfinServerSummary[] servers =
		[
			new("a", "Home", true,
			[
				Summary("1", "Sintel", JellyfinSessionPlayback.Paused),
				Summary("2", "Big Buck Bunny", JellyfinSessionPlayback.Playing)
			]),
			new("b", "Cabin", true, [Summary("3", "Elephants Dream", JellyfinSessionPlayback.Playing)])
		];

		var all = JellyfinSessionsState.Compute(servers, null);
		var home = JellyfinSessionsState.Compute(servers, "a");

		Assert.Multiple(() =>
		{
			Assert.That(all.Rows.Select(row => row.Title),
				Is.EqualTo(new[] { "Big Buck Bunny", "Elephants Dream", "Sintel" }));
			Assert.That(home.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Big Buck Bunny", "Sintel" }));
			Assert.That(home.Rows[0].Detail, Is.EqualTo("alex · TV"));
		});
	}

	[Test]
	public void The_widget_view_builds_for_every_state_and_the_sample()
	{
		JellyfinSessionsState[] states =
		[
			JellyfinSessionsState.Empty,
			new(JellyfinSessionsStatus.NotConnected, []),
			new(JellyfinSessionsStatus.Ready, []),
			JellyfinSessionsSample.State,
			JellyfinSessionsSample.State with { Rows = [JellyfinSessionsSample.State.Rows[0]] },
			JellyfinSessionsSample.State with
			{
				Rows = [JellyfinSessionsSample.State.Rows[0] with { Artwork = new MacroDeck.Ui.Model.Resources.UiResource { ResourceId = "r1" } }],
				Glyph = new MacroDeck.Ui.Model.Resources.UiResource { ResourceId = "g1" }
			}
		];

		foreach (var state in states)
		{
			Assert.DoesNotThrow(() =>
			{
				using var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
					JellyfinSessionsView.Build(new UiState<JellyfinSessionsState>(state)));
				_ = view.Tree;
			}, state.Status.ToString());
		}
	}

	[Test]
	public void The_widget_settings_build_with_and_without_servers()
	{
		var data = JsonDocument.Parse("""{"server":""}""").RootElement;

		Assert.DoesNotThrow(() =>
		{
			using var empty = new UiView(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				JellyfinSessionsUiProvider.BuildConfig(data, []));
			using var listed = new UiView(new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				JellyfinSessionsUiProvider.BuildConfig(data, [new JellyfinServerSummary("a", "Home", true, [])]));
			_ = empty.Tree;
			_ = listed.Tree;
		});
	}

	private static JellyfinSessionSummary Summary(string key, string title, JellyfinSessionPlayback playback)
		=> new(key, "alex", "TV", "Jellyfin Web", title, null, playback, 50);
}
