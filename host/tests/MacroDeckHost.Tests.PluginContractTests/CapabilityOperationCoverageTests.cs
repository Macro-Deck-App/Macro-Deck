using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Hosting.Capabilities.Calendar;
using MacroDeck.Plugin.Hosting.Capabilities.CompanionDeviceEntries;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Hosting.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Hosting.Capabilities.EmailInbox;
using MacroDeck.Plugin.Hosting.Capabilities.Events;
using MacroDeck.Plugin.Hosting.Capabilities.Icons;
using MacroDeck.Plugin.Hosting.Capabilities.Issues;
using MacroDeck.Plugin.Hosting.Capabilities.Migration;
using MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Capabilities.VirtualProfiles;
using MacroDeck.Plugin.Hosting.Capabilities.Weather;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class CapabilityOperationCoverageTests : CapabilityContractFixture
{
	[Test]
	public async Task Every_actions_operation_is_recognised()
	{
		var dynamic = new TestDynamicOptionsAction("pick");
		var plain = new TestAction("play");
		await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(plain, dynamic)])],
			[Action("play"), Action("pick")],
			[CapabilityKinds.Actions]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Actions, "play");
	}

	[Test]
	public async Task Every_variables_operation_is_recognised()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		await ConnectAsync([
				new VariablesCapabilityHandler([new TestVariableIntegration([variable])],
					TestMetadata.Default,
					new VariableSubscriptions(Serilog.Log.Logger),
					Serilog.Core.Logger.None)
			],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Variables, "cpu-temp");
	}

	[Test]
	public async Task Every_events_operation_is_recognised()
	{
		await ConnectAsync([
				new EventsCapabilityHandler([
						new TestEventIntegration("OBS Studio",
							new EventDefinition { Id = "scene-changed", Name = "Scene changed" })
					],
					TestMetadata.Default)
			],
			[Provider(CapabilityKinds.Events)],
			[CapabilityKinds.Events]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Events, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_icons_operation_is_recognised()
	{
		var bytes = new byte[] { 1, 2, 3, 4 };
		await ConnectAsync([new IconsCapabilityHandler(new IconAssetSource(bytes, "image/png"))],
			[
				new DeclaredCapability
					{ Kind = CapabilityKinds.Icons, LocalId = IconsCapabilityHandler.LocalId, VersionRange = _version }
			],
			[CapabilityKinds.Icons],
			() => UploadAssetAsync(AssetKinds.Icon, "image/png", bytes));

		await AssertNoneUnsupportedAsync(CapabilityKinds.Icons, IconsCapabilityHandler.LocalId);
	}

	[Test]
	public async Task Every_config_flow_operation_is_recognised()
	{
		await ConnectAsync([
				new ConfigFlowCapabilityHandler([new TestConfigFlowIntegration(() => new TestConfigFlow())],
					new PluginConfigFlowSessions(TimeProvider.System))
			],
			[Provider(CapabilityKinds.ConfigFlow)],
			[CapabilityKinds.ConfigFlow]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.ConfigFlow, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_music_player_operation_is_recognised()
	{
		await ConnectAsync([
				new MusicPlayerCapabilityHandler([
						new TestMusicPlayerIntegration("Spotify",
							new Dictionary<string, IMusicPlayer> { ["a1"] = new TestMusicPlayer() })
					],
					TestMetadata.Default,
					new FakeAssetUploader())
			],
			[Provider(CapabilityKinds.MusicPlayer)],
			[CapabilityKinds.MusicPlayer]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.MusicPlayer, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_weather_operation_is_recognised()
	{
		await ConnectAsync([
				new WeatherCapabilityHandler([
						new TestWeatherIntegration("Open-Meteo",
							new Dictionary<string, IWeatherStation> { ["berlin"] = new TestWeatherStation() })
					],
					TestMetadata.Default)
			],
			[Provider(CapabilityKinds.Weather)],
			[CapabilityKinds.Weather]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Weather, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_calendar_operation_is_recognised()
	{
		await ConnectAsync([
				new CalendarCapabilityHandler([new TestCalendarIntegration("Google Calendar", "alice")],
					TestMetadata.Default)
			],
			[Provider(CapabilityKinds.Calendar)],
			[CapabilityKinds.Calendar]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Calendar, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_email_inbox_operation_is_recognised()
	{
		await ConnectAsync([
				new EmailInboxCapabilityHandler([new TestEmailInboxIntegration("Example Mail", "work")],
					TestMetadata.Default)
			],
			[Provider(CapabilityKinds.EmailInbox)],
			[CapabilityKinds.EmailInbox]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.EmailInbox, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_companion_device_entries_operation_is_recognised()
	{
		await ConnectAsync([new CompanionDeviceEntriesCapabilityHandler([new CompanionDeviceEntriesIntegration()])],
			[Provider(CapabilityKinds.CompanionDeviceEntries)],
			[CapabilityKinds.CompanionDeviceEntries]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.CompanionDeviceEntries, ProviderCapabilityId.LocalId);
	}

	private sealed class CompanionDeviceEntriesIntegration : MacroDeck.Sdk.IPluginIntegration,
		MacroDeck.Sdk.Companion.ICompanionDeviceConfigEntries
	{
		public IReadOnlyList<MacroDeck.Sdk.Actions.IActionDefinition> Actions => [];

		public Task InitializeAsync(MacroDeck.Sdk.IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	[Test]
	public async Task Every_virtual_profiles_operation_is_recognised()
	{
		await ConnectAsync([
				new VirtualProfilesCapabilityHandler([new TestProfileIntegration("Spotify", [])], TestMetadata.Default)
			],
			[Provider(CapabilityKinds.VirtualProfiles)],
			[CapabilityKinds.VirtualProfiles]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.VirtualProfiles, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_issues_operation_is_recognised()
	{
		await ConnectAsync([
				new IssuesCapabilityHandler([
					new TestIssueIntegration(_ =>
						Task.FromResult<IReadOnlyList<MacroDeck.Sdk.Issues.IntegrationIssue>>([]))
				])
			],
			[Provider(CapabilityKinds.Issues)],
			[CapabilityKinds.Issues]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Issues, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_config_entries_operation_is_recognised()
	{
		await ConnectAsync([new ConfigEntriesCapabilityHandler([new ConfigEntriesIntegration()])],
			[Provider(CapabilityKinds.ConfigEntries)],
			[CapabilityKinds.ConfigEntries]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.ConfigEntries, ProviderCapabilityId.LocalId);
	}

	private sealed class ConfigEntriesIntegration : MacroDeck.Sdk.IPluginIntegration,
		MacroDeck.Sdk.ConfigFlow.IConfigEntryLifecycle
	{
		public IReadOnlyList<MacroDeck.Sdk.Actions.IActionDefinition> Actions => [];

		public Task InitializeAsync(MacroDeck.Sdk.IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	[Test]
	public async Task Every_migration_operation_is_recognised()
	{
		await ConnectAsync([
				new MigrationCapabilityHandler([
					new TestMigrationIntegration(new TestIntegrationMigration
						{ Source = MigrationSource.MacroDeck2, ClaimedActionSources = ["Some Plugin"] })
				])
			],
			[Provider(CapabilityKinds.Migration)],
			[CapabilityKinds.Migration]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Migration, ProviderCapabilityId.LocalId);
	}

	[Test]
	public async Task Every_ui_operation_is_recognised()
	{
		await ConnectAsync([new TestUiCapabilityHandler()], [Provider(CapabilityKinds.Ui)], [CapabilityKinds.Ui]);

		await AssertNoneUnsupportedAsync(CapabilityKinds.Ui, ProviderCapabilityId.LocalId);
	}

	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private static DeclaredCapability Provider(string kind)
	{
		return new DeclaredCapability
			{ Kind = kind, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version };
	}

	private static DeclaredCapability Action(string id)
	{
		return new DeclaredCapability
			{ Kind = CapabilityKinds.Actions, LocalId = id, VersionRange = _version };
	}

	private static DeclaredCapability Variable(string id)
	{
		return new DeclaredCapability
			{ Kind = CapabilityKinds.Variables, LocalId = id, VersionRange = _version };
	}

	private async Task AssertNoneUnsupportedAsync(string kind, string localId)
	{
		foreach (var operation in CapabilityOperations.For(kind))
		{
			string? code = null;

			try
			{
				await InvokeRawAsync(kind, localId, operation);
			}
			catch (RemoteCapabilityException exception)
			{
				code = exception.Code;
			}

			Assert.That(code,
				Is.Not.EqualTo(ProtocolErrorCodes.CapabilityUnsupported),
				$"'{kind}'/'{operation}' was rejected as unsupported - its handler has no case for it.");
		}
	}
}
