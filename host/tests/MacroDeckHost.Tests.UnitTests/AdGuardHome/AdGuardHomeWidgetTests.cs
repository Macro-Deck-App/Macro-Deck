using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.AdGuardHome;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeWidgetTests
{
	private const string HomeId = "6a4c0d2e-0000-0000-0000-000000000001";
	private const string OfficeId = "6a4c0d2e-0000-0000-0000-000000000002";

	private AdGuardHomeHub _hub = null!;
	private FakeHostLockState _lock = null!;
	private RecordingUiTransport _transport = null!;
	private FakeTimeProvider _time = null!;
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
		_lock = new FakeHostLockState();
		_transport = new RecordingUiTransport();
		_time = new FakeTimeProvider();
		_commands = [];
		_outcome = AdGuardHomeCommandOutcome.Succeeded;
		_hub.UseExecutor((entryId, command, _) =>
		{
			_commands.Add((entryId, command));
			return Task.FromResult(_outcome);
		});
		_provider = new AdGuardHomeUiProvider(_hub,
			_lock,
			_transport,
			new UiResourceStore(),
			new FakeIntegrationRegistry(),
			_time);
	}

	[TearDown]
	public void TearDown() => global::System.Globalization.CultureInfo.CurrentCulture = _culture;

	[Test]
	public async Task The_widget_shows_the_configured_instance_with_its_statistics()
	{
		_hub.Replace([Connected(HomeId, "Home", queries: 1234), Connected(OfficeId, "Office", queries: 98765)]);

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
		_hub.Replace([Connected(HomeId, "Home", queries: 1234)]);
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
		_hub.Replace([Connected(OfficeId, "Office", queries: 5)]);

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
		Assert.That(_commands.Single(), Is.EqualTo((OfficeId,
			new AdGuardHomeCommand(AdGuardHomeCommandKind.PauseProtection, TimeSpan.FromHours(1)))));
	}

	[Test]
	public async Task A_paused_instance_offers_to_enable_protection_and_counts_down()
	{
		_hub.Replace([Connected(HomeId, "Home") with { ProtectionEnabled = false, DisabledUntil = _time.Now.AddMinutes(5) }]);
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
	public async Task A_locked_host_refuses_a_press_and_tells_the_client()
	{
		_hub.Replace([Connected(HomeId, "Home")]);
		_lock.IsLocked = true;
		await using var session = await OpenAsync(new { instance = HomeId, durations = new[] { "5m" } }, widgetId: Guid.NewGuid());

		((MacroDeckHost.Application.Ui.Sessions.InProcess.IOriginAwareUiSession)session).Dispatch(
			new MacroDeck.Ui.Model.Events.UiEvent
			{
				NodeId = Button(session, "pause-5m").Id,
				Name = MacroDeck.Ui.Components.UiComponentEvents.Press
			},
			"client-1");

		Assert.Multiple(() =>
		{
			Assert.That(_commands, Is.Empty);
			Assert.That(_transport.GroupMessages.Select(message => message.Message).OfType<ActionExecutionStatusEvent>()
				.Single().Error!.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
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
			.Any(text => text.Contains("Sign-in rejected", StringComparison.Ordinal)), "the failure was not shown");
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
					[UiConfigSurfaceAttributes.EntryPoint] = JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
					[UiConfigSurfaceAttributes.WidgetType] =
						JsonSerializer.SerializeToElement(AdGuardHomeWidgetType.QualifiedId),
				},
			},
			UiModelVersion = 1,
		}, CancellationToken.None))!;

		var json = JsonSerializer.Serialize(session.BuildTree().Root);
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain(HomeId));
			Assert.That(json, Does.Contain(OfficeId));
		});
	}

	private static AdGuardHomeSnapshot Connected(string entryId, string title, long queries = 100)
		=> new(entryId, title, title.ToLowerInvariant(), AdGuardHomeConnection.Connected)
		{
			ProtectionEnabled = true,
			DnsRunning = true,
			Version = "v0.107.57",
			Statistics = new AdGuardHomeStatistics(queries, queries / 4, 1, 2, 3, 4.2),
		};

	private async Task<IUiSession> OpenAsync(object data, Guid? widgetId = null)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(AdGuardHomeWidgetType.QualifiedId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data),
		};

		if (widgetId is { } id)
		{
			attributes[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(id.ToString());
		}

		return (await _provider.CreateSessionAsync(new UiSessionRequest
		{
			Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared, Attributes = attributes },
			UiModelVersion = 1,
		}, CancellationToken.None))!;
	}

	private static UiNode Button(IUiSession session, string key)
		=> CalendarTree.Flatten(session.BuildTree().Root)
			.First(node => node.Id.EndsWith("." + key, StringComparison.Ordinal) && CalendarTree.IsPressable(node));

	private static List<string> Buttons(UiNode root)
		=> [.. CalendarTree.Flatten(root).Where(CalendarTree.IsPressable).Select(node => node.Id)];
}
