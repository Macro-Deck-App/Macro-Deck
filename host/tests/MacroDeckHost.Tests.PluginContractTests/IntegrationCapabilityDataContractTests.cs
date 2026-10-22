using MacroDeck.Plugin.Hosting.Capabilities.BatteryProvider;
using MacroDeck.Plugin.Hosting.Capabilities.Calendar;
using MacroDeck.Plugin.Hosting.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class IntegrationCapabilityDataContractTests : CapabilityContractFixture
{
	private BatteryRegistry _batteries = null!;
	private EmailSyncSignal _signal = null!;
	private EmailInboxRegistry _inboxRegistry = null!;
	private EmailInboxCache _inboxes = null!;
	private CalendarEventCache _calendars = null!;

	[SetUp]
	public async Task ConnectProviderPlugin()
	{
		var now = Time.GetUtcNow();
		var calendar = new TestCalendarIntegration("Example Calendar", "alice")
		{
			Calendars =
			{
				["alice"] =
				[
					new CalendarInfo { Id = "work", Name = "Work", IsPrimary = true },
					new CalendarInfo { Id = "home", Name = "Home" }
				]
			},
			Events =
			{
				["alice"] =
				[
					new CalendarEvent
					{
						Id = "standup",
						CalendarId = "work",
						Title = "Stand-up",
						Start = now.AddHours(1),
						End = now.AddHours(1).AddMinutes(15),
						MeetingUrl = "https://meet.example.com/abc"
					}
				]
			}
		};
		var mail = new TestEmailInboxIntegration("Example Mail", "work")
		{
			Snapshots =
			{
				["work"] = new EmailInboxSnapshot
				{
					Status = EmailInboxStatus.Connected,
					UnreadCount = 1,
					TotalCount = 3,
					SyncedAt = now,
					Messages = [new EmailMessage { Id = "1", Subject = "Hello", ReceivedAt = now.AddMinutes(-1) }]
				}
			}
		};

		await ConnectAsync([
				new CalendarCapabilityHandler([calendar], TestMetadata.Default),
				new EmailInboxCapabilityHandler([mail], TestMetadata.Default),
				new BatteryProviderCapabilityHandler([new TestBatteryIntegration()], TestMetadata.Default)
			],
			[
				Declared(CapabilityKinds.Calendar), Declared(CapabilityKinds.EmailInbox),
				Declared(CapabilityKinds.BatteryProvider)
			],
			[CapabilityKinds.Calendar, CapabilityKinds.EmailInbox, CapabilityKinds.BatteryProvider]);

		_batteries = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_batteries.Publish(PluginId,
			null,
			[
				new BatteryDevice
					{ Id = "pad", Name = "Pad", LevelPercent = 55, ChargeState = BatteryChargeState.Charging },
				new BatteryDevice { Id = "mouse", Name = "Mouse", LevelPercent = 12 }
			]);
		_batteries.Publish("com.example.other", null, [new BatteryDevice { Id = "watch", Name = "Watch" }]);

		_calendars = new CalendarEventCache(new CalendarRegistry(IntegrationRegistry, Serilog.Core.Logger.None),
			Time,
			Serilog.Core.Logger.None,
			TimeZoneInfo.Utc);
		await _calendars.SyncAsync(CancellationToken.None);

		_signal = new EmailSyncSignal();
		_inboxRegistry = new EmailInboxRegistry(IntegrationRegistry, _signal, Serilog.Core.Logger.None);
		_inboxes = new EmailInboxCache(_inboxRegistry, new InMemoryKeys(), _signal, Time, Serilog.Core.Logger.None);
		await _inboxes.SyncAsync(EmailSyncRequest.Everything, CancellationToken.None);
	}

	[TearDown]
	public void DisposeProviders()
	{
		_inboxes.Dispose();
		_inboxRegistry.Dispose();
		_signal.Dispose();
		_calendars.Dispose();
		_batteries.Dispose();
	}

	[Test]
	public async Task A_plugin_lists_only_its_own_batteries_under_its_integration_id()
	{
		var response = await new GetIntegrationBatteriesRequestMessageHandler(IntegrationRegistry, _batteries)
			.Handle(new GetIntegrationBatteriesRequest { IntegrationId = PluginId }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Found, Is.True);
			Assert.That(response.Available, Is.True);
			Assert.That(response.Batteries.Select(battery => battery.Name), Is.EqualTo(new[] { "Mouse", "Pad" }));
			Assert.That(response.Batteries.Single(battery => battery.Name == "Pad").ChargeState,
				Is.EqualTo(BatteryChargeState.Charging));
		});
	}

	[Test]
	public async Task A_plugin_calendar_lists_its_calendars_and_events()
	{
		var response = await new GetIntegrationCalendarsRequestMessageHandler(IntegrationRegistry, _calendars)
			.Handle(new GetIntegrationCalendarsRequest { IntegrationId = PluginId }, CancellationToken.None);

		var account = response.Accounts.Single();
		var meeting = response.Events.Single();
		Assert.Multiple(() =>
		{
			Assert.That(account.Calendars.Select(calendar => calendar.Name), Is.EquivalentTo(new[] { "Work", "Home" }));
			Assert.That(account.Calendars.Single(calendar => calendar.IsPrimary).Name, Is.EqualTo("Work"));
			Assert.That(meeting.Title, Is.EqualTo("Stand-up"));
			Assert.That(meeting.HasMeetingUrl, Is.True);
			Assert.That(meeting.CalendarKey, Is.EqualTo(account.Calendars.Single(calendar => calendar.IsPrimary).Key));
		});
	}

	[Test]
	public async Task A_plugin_inbox_lists_its_counts_and_messages()
	{
		var response = await new GetIntegrationEmailInboxesRequestMessageHandler(IntegrationRegistry, _inboxes)
			.Handle(new GetIntegrationEmailInboxesRequest { IntegrationId = PluginId }, CancellationToken.None);

		var inbox = response.Inboxes.Single();
		Assert.Multiple(() =>
		{
			Assert.That(inbox.Status, Is.EqualTo(EmailInboxStatus.Connected));
			Assert.That(inbox.UnreadCount, Is.EqualTo(1));
			Assert.That(inbox.TotalCount, Is.EqualTo(3));
			Assert.That(inbox.Messages.Single().Subject, Is.EqualTo("Hello"));
		});
	}

	[Test]
	public async Task The_counts_are_the_entities_each_capability_provides()
	{
		var counter = new IntegrationCapabilityCounter(_batteries,
			_calendars,
			_inboxes,
			new WeatherRegistry(IntegrationRegistry, Serilog.Core.Logger.None),
			new MusicPlayerRegistry(IntegrationRegistry, Serilog.Core.Logger.None),
			new EventRegistry(IntegrationRegistry, [], Serilog.Core.Logger.None),
			new WidgetTypeRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ContractDeviceRepository());
		var integration = IntegrationRegistry.Integrations.Single(candidate => candidate.Id == PluginId);

		var counts = await counter.CountAsync(integration, CancellationToken.None);

		Assert.That(counts,
			Is.EquivalentTo(new Dictionary<string, int>
			{
				[CapabilityKinds.BatteryProvider] = 2,
				[CapabilityKinds.Calendar] = 2,
				[CapabilityKinds.EmailInbox] = 1
			}));
	}

	[Test]
	public async Task An_unknown_integration_is_reported_as_not_found()
	{
		var response = await new GetIntegrationBatteriesRequestMessageHandler(IntegrationRegistry, _batteries)
			.Handle(new GetIntegrationBatteriesRequest { IntegrationId = "com.example.missing" },
				CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Found, Is.False);
			Assert.That(response.Batteries, Is.Empty);
		});
	}

	[Test]
	public async Task A_disconnected_plugin_reports_its_data_as_unavailable()
	{
		Disconnect();

		var response = await new GetIntegrationEmailInboxesRequestMessageHandler(IntegrationRegistry, _inboxes)
			.Handle(new GetIntegrationEmailInboxesRequest { IntegrationId = PluginId }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Found, Is.True);
			Assert.That(response.Available, Is.False);
		});
	}

	private static DeclaredCapability Declared(string kind)
	{
		return new DeclaredCapability
		{
			Kind = kind,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};
	}

	private sealed class TestBatteryIntegration : IPluginIntegration, IBatteryProvider
	{
		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public string ProviderName => "Example Batteries";

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class InMemoryKeys : IEmailInboxKeyStore
	{
		private EmailInboxKeySet _keys = EmailInboxKeySet.Empty;

		public IReadOnlyList<EmailInboxKey> Keys => _keys.Keys;

		public IReadOnlyDictionary<string, string> Assign(IReadOnlyList<EmailInboxDescriptor> visible)
		{
			_keys = EmailInboxKeys.Assign(_keys, visible);
			return _keys.Keys.ToDictionary(key => key.Ref, key => key.Slug, StringComparer.Ordinal);
		}
	}
}
