using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Integrations.Ping.Connections;
using MacroDeckHost.Integrations.Ping.State;
using MacroDeckHost.Integrations.Ping.Variables;
using MacroDeckHost.Integrations.Ping.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;

namespace MacroDeckHost.Tests.UnitTests.Ping;

[TestFixture]
internal sealed class PingWidgetTests
{
	private static readonly Guid _nas = Guid.Parse("6a4c0d2e-0000-0000-0000-000000000001");
	private static readonly Guid _router = Guid.Parse("6a4c0d2e-0000-0000-0000-000000000002");

	private PingHub _hub = null!;
	private FakePingContext _context = null!;
	private PingUiProvider _provider = null!;
	private CultureInfo _culture = null!;

	[SetUp]
	public void SetUp()
	{
		_culture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("en-US");
		_hub = new PingHub();
		_context = new FakePingContext();
		_provider = new PingUiProvider(_hub, () => _context);
	}

	[TearDown]
	public void TearDown()
	{
		CultureInfo.CurrentCulture = _culture;
	}

	[Test]
	public async Task The_widget_shows_the_chosen_target_with_its_latency()
	{
		_hub.Replace([Online(_nas, "NAS", 12), Online(_router, "Router", 3)]);

		await using var session = await OpenAsync(_provider, new { target = _router.ToString("D") });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Router"));
			Assert.That(texts, Does.Contain("3 ms"));
			Assert.That(texts, Has.None.EqualTo("NAS"));
		});
	}

	[Test]
	public async Task The_widget_follows_the_target_going_offline()
	{
		_hub.Replace([Online(_nas, "NAS", 12)]);
		await using var session = await OpenAsync(_provider, new { });

		_hub.Update(new PingSnapshot(_nas, "NAS", "nas", "nas.lan")
		{
			Reachability = PingReachability.Offline, Problem = PingProblem.TimedOut, PacketLossPercent = 100
		});
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Offline"));
			Assert.That(texts, Does.Contain("No reply"));
			Assert.That(texts, Has.None.EqualTo("12 ms"));
		});
	}

	[Test]
	public async Task Packet_loss_shows_while_the_target_is_online()
	{
		_hub.Replace([Online(_nas, "NAS", 12) with { PacketLossPercent = 15 }]);

		await using var session = await OpenAsync(_provider, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("15 % packet loss"));
	}

	[Test]
	public async Task A_missing_ping_permission_is_shown_once_before_the_first_reply()
	{
		_hub.Replace([
			new PingSnapshot(_nas, "NAS", "nas", "nas.lan") { Problem = PingProblem.NotPermitted }
		]);

		await using var session = await OpenAsync(_provider, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root).Count(text => text == "Ping not permitted"),
			Is.EqualTo(1));
	}

	[Test]
	public async Task A_missing_ping_permission_is_shown_on_a_target_that_was_online()
	{
		_hub.Replace([Online(_nas, "NAS", 12) with { Problem = PingProblem.NotPermitted }]);

		await using var session = await OpenAsync(_provider, new { });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("12 ms"));
			Assert.That(texts.Count(text => text == "Ping not permitted"), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task The_widget_says_so_when_no_target_is_configured()
	{
		await using var session = await OpenAsync(_provider, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("No ping target"));
	}

	[Test]
	public async Task Packet_loss_shows_at_zero_percent_when_enabled()
	{
		_hub.Replace([Online(_nas, "NAS", 12)]);

		await using var shown = await OpenAsync(_provider, new { showPacketLoss = true });
		await using var hidden = await OpenAsync(_provider, new { showPacketLoss = false });
		var shownTexts = CalendarTree.Texts(shown.BuildTree().Root);
		var hiddenTexts = CalendarTree.Texts(hidden.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(shownTexts, Does.Contain("0 % packet loss"));
			Assert.That(hiddenTexts, Has.None.Contains("packet loss"));
			Assert.That(hiddenTexts, Does.Contain("Online"));
		});
	}

	[Test]
	public async Task The_chart_advances_every_sample_while_the_latency_stays_the_same()
	{
		_hub.Replace([Online(_nas, "NAS", 4)]);
		await using var session = await OpenAsync(_provider, new { });
		await CalendarTree.WaitForAsync(() => _context.OwnVariables.OpenWindows.Count == 1,
			"the widget never opened a latency history window");

		_context.OwnVariables.RecordSample(Latency("nas"), 4);
		_context.OwnVariables.RecordSample(Latency("nas"), 4);
		await CalendarTree.WaitForAsync(() => Points(session).Length == 2, "the chart never drew two samples");

		_context.OwnVariables.RecordSample(Latency("nas"), 4);
		await CalendarTree.WaitForAsync(() => Points(session).Length == 3,
			"the chart did not advance on an unchanged sample");
	}

	[Test]
	public async Task The_chart_scales_latency_from_zero()
	{
		_hub.Replace([Online(_nas, "NAS", 4)]);
		await using var session = await OpenAsync(_provider, new { });
		await CalendarTree.WaitForAsync(() => _context.OwnVariables.OpenWindows.Count == 1,
			"the widget never opened a latency history window");

		_context.OwnVariables.RecordSample(Latency("nas"), 3);
		_context.OwnVariables.RecordSample(Latency("nas"), null);
		_context.OwnVariables.RecordSample(Latency("nas"), 4);
		await CalendarTree.WaitForAsync(() => Points(session).Length == 2, "the chart never drew the samples");

		Assert.That(Points(session), Is.EqualTo(new[] { 0.75, 1 }));
	}

	[TestCase(10, 300)]
	[TestCase(60, 600)]
	public async Task The_chart_covers_about_the_last_thirty_pings(int intervalSeconds, int samples)
	{
		_hub.Replace([Online(_nas, "NAS", 4) with { Interval = TimeSpan.FromSeconds(intervalSeconds) }]);
		await using var session = await OpenAsync(_provider, new { });
		await CalendarTree.WaitForAsync(() => _context.OwnVariables.OpenWindows.Count == 1,
			"the widget never opened a latency history window");

		for (var i = 0; i < samples + 5; i++)
		{
			_context.OwnVariables.RecordSample(Latency("nas"), 4);
		}

		await CalendarTree.WaitForAsync(() => Points(session).Length == samples,
			"the chart does not hold the expected span");
		Assert.That(_context.OwnVariables.OpenWindows.Single().Capacity, Is.EqualTo(samples));
	}

	[Test]
	public async Task A_retuned_target_moves_the_chart_to_its_new_variable()
	{
		_hub.Replace([Online(_nas, "NAS", 4)]);
		await using var session = await OpenAsync(_provider, new { target = _nas.ToString("D") });
		await CalendarTree.WaitForAsync(() => _context.OwnVariables.OpenWindows.Count == 1,
			"the widget never opened a latency history window");

		_hub.Update(Online(_nas, "NAS", 4) with { Key = "storage" });

		await CalendarTree.WaitForAsync(()
				=> _context.OwnVariables.OpenWindows.Select(window => window.Name).SequenceEqual([Latency("storage")]),
			"the widget did not move to the retuned target's variable");
	}

	[Test]
	public async Task Closing_the_widget_releases_its_history_window()
	{
		_hub.Replace([Online(_nas, "NAS", 4)]);
		var session = await OpenAsync(_provider, new { });
		await CalendarTree.WaitForAsync(() => _context.OwnVariables.OpenWindows.Count == 1,
			"the widget never opened a latency history window");

		await session.DisposeAsync();

		Assert.That(_context.OwnVariables.OpenWindows, Is.Empty);
	}

	[Test]
	public async Task The_sample_renders_without_any_target()
	{
		await using var session = await OpenAsync(_provider, new { }, true);

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("4 ms"));
	}

	internal static async Task<IUiSession> OpenAsync(IUiProvider provider, object data, bool sample = false)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(PingWidgetType.QualifiedId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data)
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return (await provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = sample ? UiSurfaceKinds.Preview : UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					Attributes = attributes
				},
				UiModelVersion = 1
			},
			CancellationToken.None))!;
	}

	private static string Latency(string key)
	{
		return PingVariables.Name(key, PingVariables.Latency);
	}

	private static double[] Points(IUiSession session)
	{
		var chart = CalendarTree.Flatten(session.BuildTree().Root)
			.FirstOrDefault(node => node.Type == UiComponents.Chart);
		return chart is not null && chart.Properties.TryGetValue(UiComponentProperties.Points, out var points)
			? points.Deserialize<double[]>()!
			: [];
	}

	private static PingSnapshot Online(Guid entryId, string title, long latency)
	{
		return new PingSnapshot(entryId, title, title.ToLowerInvariant(), $"{title.ToLowerInvariant()}.lan")
		{
			Reachability = PingReachability.Online,
			LatencyMilliseconds = latency,
			PacketLossPercent = 0,
			Interval = TimeSpan.FromSeconds(10)
		};
	}
}
