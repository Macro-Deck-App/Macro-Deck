using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome.Connections;
using MacroDeckHost.Integrations.AdGuardHome.State;
using MacroDeckHost.Integrations.AdGuardHome.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeWidgetTests
{
	private const string HomeId = "6a4c0d2e-0000-0000-0000-000000000001";
	private const string OfficeId = "6a4c0d2e-0000-0000-0000-000000000002";

	private AdGuardHomeHub _hub = null!;
	private FakeTimeProvider _time = null!;
	private BuiltInUiResources _resources = null!;
	private List<(string EntryId, AdGuardHomeCommand Command)> _commands = null!;
	private AdGuardHomeCommandOutcome _outcome;
	private AdGuardHomeUiProvider _provider = null!;
	private global::System.Globalization.CultureInfo _culture = null!;

	[SetUp]
	public void SetUp()
	{
		_culture = global::System.Globalization.CultureInfo.CurrentCulture;
		global::System.Globalization.CultureInfo.CurrentCulture = new global::System.Globalization.CultureInfo("en-US");
		_hub = new AdGuardHomeHub();
		_time = new FakeTimeProvider();
		_commands = [];
		_outcome = AdGuardHomeCommandOutcome.Succeeded;
		_hub.UseExecutor((entryId, command, _) =>
		{
			_commands.Add((entryId, command));
			return Task.FromResult(_outcome);
		});
		_resources = new BuiltInUiResources(AdGuardHomeIntegration.IntegrationId,
			new UiResourceStore(),
			typeof(AdGuardHomeIntegration));
		_provider = new AdGuardHomeUiProvider(_hub, () => _resources, _time);
	}

	[TearDown]
	public void TearDown()
	{
		global::System.Globalization.CultureInfo.CurrentCulture = _culture;
	}

	[Test]
	public async Task The_widget_shows_the_configured_instance_with_its_statistics()
	{
		_hub.Replace([Connected(HomeId, "Home", 1234), Connected(OfficeId, "Office", 98765)]);

		await using var session = await OpenAsync(new { instance = OfficeId, view = "statistics" });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Office"));
			Assert.That(texts, Does.Contain("98.7K"));
			Assert.That(texts, Has.None.EqualTo("1,234"));
		});
	}

	[TestCase(AdGuardHomeConnection.Unreachable)]
	[TestCase(AdGuardHomeConnection.Unauthorized)]
	[TestCase(AdGuardHomeConnection.Timeout)]
	[TestCase(AdGuardHomeConnection.Incompatible)]
	public async Task An_instance_that_is_not_connected_shows_no_stale_values(AdGuardHomeConnection failure)
	{
		_hub.Replace([Connected(HomeId, "Home", 1234)]);
		await using var session = await OpenAsync(new { instance = HomeId, view = "overview" });
		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("1,234"));

		_hub.Update(new AdGuardHomeSnapshot(HomeId, "Home", "home", failure));
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Has.None.EqualTo("1,234"));
			Assert.That(texts, Has.None.EqualTo("Protection on"));
			Assert.That(Buttons(session.BuildTree().Root), Is.Empty);
		});
	}

	[Test]
	public async Task A_widget_whose_instance_was_removed_says_so()
	{
		_hub.Replace([Connected(OfficeId, "Office", 5)]);

		await using var session = await OpenAsync(new { instance = HomeId });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root),
			Has.Some.Contains("no longer exists"));
	}

	[Test]
	public async Task Without_any_instance_the_widget_asks_for_one()
	{
		await using var session = await OpenAsync(new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root),
			Has.Some.Contains("Add an AdGuard Home instance"));
	}

	[Test]
	public async Task A_pause_button_pauses_the_configured_instance_for_its_duration()
	{
		_hub.Replace([Connected(HomeId, "Home"), Connected(OfficeId, "Office")]);
		await using var session = await OpenAsync(new { instance = OfficeId, durations = new[] { "1h", "8h" } });

		CalendarTree.Press(session, Button(session, "pause-1h"));

		await CalendarTree.WaitForAsync(() => _commands.Count == 1, "no command was sent");
		Assert.That(_commands.Single(),
			Is.EqualTo((OfficeId,
				new AdGuardHomeCommand(AdGuardHomeCommandKind.PauseProtection, TimeSpan.FromHours(1)))));
	}

	[Test]
	public async Task A_paused_instance_offers_to_enable_protection_and_counts_down()
	{
		_hub.Replace([
			Connected(HomeId, "Home") with { ProtectionEnabled = false, DisabledUntil = _time.Now.AddMinutes(5) }
		]);
		await using var session = await OpenAsync(new { instance = HomeId });

		CalendarTree.Press(session, Button(session, "enable"));

		await CalendarTree.WaitForAsync(() => _commands.Count == 1, "no command was sent");
		Assert.Multiple(() =>
		{
			Assert.That(_commands.Single().Command.Kind, Is.EqualTo(AdGuardHomeCommandKind.EnableProtection));
			Assert.That(CalendarTree.Texts(session.BuildTree().Root), Has.Some.Contains("resumes in 5 minutes"));
			Assert.That(Buttons(session.BuildTree().Root).Any(id => id.Contains("pause-")), Is.False);
		});
	}

	[Test]
	public async Task A_failed_command_is_reported_to_the_client_that_pressed()
	{
		_hub.Replace([Connected(HomeId, "Home")]);
		_outcome = AdGuardHomeCommandOutcome.Timeout;
		await using var session = await OpenAsync(new { instance = HomeId, durations = new[] { "5m" } });
		var origin = new RecordingOrigin();

		session.Dispatch(new UiEvent { NodeId = Button(session, "pause-5m").Id, Name = UiComponentEvents.Press },
			origin);

		await CalendarTree.WaitForAsync(() => origin.Failures.Count == 1, "the failure was not reported to the client");
		Assert.That(CalendarTree.Resolve(origin.Failures.Single()), Does.Contain("did not answer in time"));
	}

	[Test]
	public async Task The_widget_shows_its_bundled_icons()
	{
		_hub.Replace([Connected(HomeId, "Home")]);

		await using var session = await OpenAsync(new { instance = HomeId, view = "statistics" });
		var json = JsonSerializer.Serialize(session.BuildTree().Root);
		var logo = await _resources.GetPluginIconAsync("widget", "logo");
		var queries = await _resources.GetPluginIconAsync("widget", "dns-queries");

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain(logo.ResourceId));
			Assert.That(json, Does.Contain(queries.ResourceId));
		});
	}

	[Test]
	public async Task A_failed_command_is_shown_on_the_widget()
	{
		_hub.Replace([Connected(HomeId, "Home")]);
		_outcome = AdGuardHomeCommandOutcome.Unauthorized;
		await using var session = await OpenAsync(new { instance = HomeId, durations = new[] { "5m" } });

		CalendarTree.Press(session, Button(session, "pause-5m"));

		await CalendarTree.WaitForAsync(() => CalendarTree.Texts(session.BuildTree().Root)
				.Any(text => text.Contains("Sign-in rejected", StringComparison.Ordinal)),
			"the failure was not shown");
	}

	[Test]
	public async Task The_configuration_lists_every_instance()
	{
		_hub.Replace([Connected(HomeId, "Home"), Connected(OfficeId, "Office")]);

		await using var session = (await _provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Config,
					SessionMode = UiSessionModes.Exclusive,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiConfigSurfaceAttributes.EntryPoint]
							= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
						[UiConfigSurfaceAttributes.WidgetType] =
							JsonSerializer.SerializeToElement(AdGuardHomeWidgetType.QualifiedId)
					}
				},
				UiModelVersion = 1
			},
			CancellationToken.None))!;

		var json = JsonSerializer.Serialize(session.BuildTree().Root);
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain(HomeId));
			Assert.That(json, Does.Contain(OfficeId));
		});
	}

	private static AdGuardHomeSnapshot Connected(string entryId, string title, long queries = 100)
	{
		return new AdGuardHomeSnapshot(entryId, title, title.ToLowerInvariant(), AdGuardHomeConnection.Connected)
		{
			ProtectionEnabled = true,
			DnsRunning = true,
			Version = "v0.107.57",
			Statistics = new AdGuardHomeStatistics(queries, queries / 4, 1, 2, 3, 4.2)
		};
	}

	private sealed class RecordingOrigin : IUiEventOrigin
	{
		private readonly Lock _gate = new();
		private readonly List<LocalizedText> _failures = [];

		public string? ClientId => "client-1";

		public IReadOnlyList<LocalizedText> Failures
		{
			get
			{
				lock (_gate)
				{
					return [.. _failures];
				}
			}
		}

		public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				_failures.Add(message);
			}

			return Task.FromResult(true);
		}

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(
			ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(ModalResult.FromCancellation<T>());
		}
	}

	private async Task<IUiSession> OpenAsync(object data, Guid? widgetId = null)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType]
				= JsonSerializer.SerializeToElement(AdGuardHomeWidgetType.QualifiedId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data)
		};

		if (widgetId is { } id)
		{
			attributes[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(id.ToString());
		}

		return (await _provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = new UiSurface
					{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared, Attributes = attributes },
				UiModelVersion = 1
			},
			CancellationToken.None))!;
	}

	private static UiNode Button(IUiSession session, string key)
	{
		return CalendarTree.Flatten(session.BuildTree().Root)
			.First(node => node.Id.EndsWith("." + key, StringComparison.Ordinal) && CalendarTree.IsPressable(node));
	}

	private static List<string> Buttons(UiNode root)
	{
		return [.. CalendarTree.Flatten(root).Where(CalendarTree.IsPressable).Select(node => node.Id)];
	}
}
