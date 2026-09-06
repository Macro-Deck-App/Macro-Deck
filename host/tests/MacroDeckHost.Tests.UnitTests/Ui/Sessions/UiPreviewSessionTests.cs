using System.Reflection;
using System.Text;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Widgets.DeveloperPreviews;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Covers the developer preview session path: discovery does not run a scenario, opening one renders the
/// scenario's real view on the new <see cref="UiSurfaceKinds.DeveloperPreview" /> surface, a refresh always
/// rebuilds it from scratch, ending a preview really stops what its scenario started, a malformed or
/// throwing scenario costs only itself, a production <see cref="IUiProvider" /> is never reached by it, and
/// opening one is gated to an admin session before anything is built.
/// </summary>
[TestFixture]
internal sealed class UiPreviewSessionTests : UiSessionFixture
{
	private static readonly string[] _discoveryScenarioNames = ["Default", "Connected", "Long text"];

	private static readonly string[] _faultyScenarioNames = ["Default", "Boom"];

	private TestUiPreviewSource _previewSource = null!;
	private UiPreviewProviderRegistry _previewProviders = null!;
	private UiPreviewSessionOpener _opener = null!;

	[SetUp]
	public void PreviewSetUp()
	{
		_previewSource = new TestUiPreviewSource(typeof(UiPreviewSessionTests).Assembly);
		_previewProviders = new UiPreviewProviderRegistry([_previewSource], () => Broker, Serilog.Core.Logger.None);

		var baseResolver = Resolver.Fallback!;
		Resolver.Fallback = new DelegatingResolver(id => baseResolver.Resolve(id) ?? _previewProviders.Resolve(id));

		_opener = new UiPreviewSessionOpener([_previewSource],
			Integrations,
			new EmptyRemotePluginSnapshotStore(),
			Registry,
			Broker);
	}

	private string FindId(string view, string scenario)
		=> _previewSource.Previews.First(preview =>
			string.Equals(preview.View, view, StringComparison.Ordinal) &&
			string.Equals(preview.Scenario, scenario, StringComparison.Ordinal)).Id;

	private async Task<string> PressAndSettleAsync(string sessionId, string connectionId)
	{
		Broker.SendEvent(new UiSendEventRequest
				{ SessionId = sessionId, NodeId = "button", Name = UiComponentEvents.Press },
			connectionId);
		await SettleAsync();
		return connectionId;
	}

	private async Task<string> TreeJsonAsync(string sessionId, string connectionId)
	{
		Attach(sessionId, connectionId, DeviceA);
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>(connectionId).Count == 1,
			"No tree arrived for the attaching connection.");

		return Encoding.UTF8.GetString(MessagesFor<UiSessionTreeUpdatedEvent>(connectionId)[0].Tree.Utf8.ToArray());
	}

	private static class SpotifyConfigView
	{
		public static int Invocations;

		[UiPreview("Default")]
		public static UiTextRun DefaultScenario()
		{
			Invocations++;
			return new UiTextRun { Key = "marker", Text = UiText.Of("x") };
		}

		[UiPreview("Connected")]
		public static UiTextRun ConnectedScenario()
		{
			Invocations++;
			return new UiTextRun { Key = "marker", Text = UiText.Of("x") };
		}
	}

	private static class WeatherDetailsView
	{
		public static int Invocations;

		[UiPreview("Long text")]
		public static UiTextRun LongTextScenario()
		{
			Invocations++;
			return new UiTextRun { Key = "marker", Text = UiText.Of("x") };
		}
	}

	[Test]
	public void Discovery_lists_scenarios_grouped_by_view_without_invoking_them()
	{
		SpotifyConfigView.Invocations = 0;
		WeatherDetailsView.Invocations = 0;

		var result = UiPreviewCatalog.Scan(typeof(UiPreviewSessionTests).Assembly);

		// Filtered to these two declaring types rather than asserted over the whole scan: the assembly also
		// carries every other scenario this file declares for the tests below it.
		var relevant = result.Registrations
			.Where(registration =>
				registration.Declaration.View is nameof(SpotifyConfigView) or nameof(WeatherDetailsView))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(relevant, Has.Count.EqualTo(3));
			Assert.That(relevant.Select(registration => registration.Declaration.View).Distinct().Count(),
				Is.EqualTo(2));
			Assert.That(relevant.Select(registration => registration.Declaration.Scenario),
				Is.EquivalentTo(_discoveryScenarioNames));
			Assert.That(relevant.Select(registration => registration.Declaration.Id).Distinct().Count(),
				Is.EqualTo(3));
			Assert.That(SpotifyConfigView.Invocations, Is.Zero, "Discovery ran a scenario's body.");
			Assert.That(WeatherDetailsView.Invocations, Is.Zero, "Discovery ran a scenario's body.");
		});
	}

	private static class MarkerScenarioView
	{
		public static int Invocations;

		[UiPreview("Default")]
		public static UiTextRun Render()
		{
			Invocations++;
			return new UiTextRun { Key = "marker", Text = UiText.Of("scenario-marker") };
		}
	}

	[Test]
	public async Task Opening_a_preview_renders_the_scenarios_real_view()
	{
		MarkerScenarioView.Invocations = 0;
		var previewId = FindId(nameof(MarkerScenarioView), "Default");

		var ticket = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = previewId }, DeviceA, isAdmin: true);
		Assert.That(ticket.Accepted, Is.True, ticket.Message);
		await ticket.Ready;

		var snapshot = Registry.Find(ticket.SessionId)!;
		var json = await TreeJsonAsync(ticket.SessionId, "c1");

		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("scenario-marker"));
			Assert.That(snapshot.Surface.Kind, Is.EqualTo(UiSurfaceKinds.DeveloperPreview));
			Assert.That(snapshot.Surface.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(MarkerScenarioView.Invocations, Is.EqualTo(1));
		});
	}

	private static class RefreshScenarioView
	{
		public static int Invocations;

		[UiPreview("Default")]
		public static UiButton Build()
		{
			Invocations++;
			var counter = new UiState<int>(0);

			return new UiButton
			{
				Key = "button",
				Events = [UiEventHandler.On(UiComponentEvents.Press, () => counter.Value++)],
				Children =
				[
					new UiTextRun { Key = "label", Text = UiText.From(() => $"count:{counter.Value}") },
				],
			};
		}
	}

	[Test]
	public async Task Refresh_recreates_the_scenario_from_scratch()
	{
		RefreshScenarioView.Invocations = 0;
		var previewId = FindId(nameof(RefreshScenarioView), "Default");
		var request = new OpenUiPreviewSessionRequest { PreviewId = previewId };

		var first = _opener.Open(request, DeviceA, isAdmin: true);
		await first.Ready;

		// A preview session is exclusive, so reading the tree again means detaching first: one connection
		// drives the scenario, and the next attach is what re-delivers the tree it has reached.
		await TreeJsonAsync(first.SessionId, "c1");
		await PressAndSettleAsync(first.SessionId, "c1");
		await PressAndSettleAsync(first.SessionId, "c1");
		Broker.Detach(first.SessionId, "c1");
		Assert.That(await TreeJsonAsync(first.SessionId, "c2"), Does.Contain("count:2"));

		var second = _opener.Open(request, DeviceA, isAdmin: true);
		await second.Ready;

		Assert.That(second.SessionId, Is.Not.EqualTo(first.SessionId), "Refresh reused the old session.");
		Assert.That(await TreeJsonAsync(second.SessionId, "c4"), Does.Contain("count:0"));
		Assert.That(RefreshScenarioView.Invocations, Is.EqualTo(2));

		await PressAndSettleAsync(second.SessionId, "c4");
		Broker.Detach(second.SessionId, "c4");
		var json = await TreeJsonAsync(second.SessionId, "c6");
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("count:1"));
			Assert.That(json, Does.Not.Contain("count:3"));
		});
	}

	private sealed class TrackingDisposable : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	private static class DisposalPrimaryView
	{
		public static List<TrackingDisposable> Created = [];

		[UiPreview("Default")]
		public static UiPreview Default()
		{
			var mock = new TrackingDisposable();
			Created.Add(mock);
			return UiPreview.Of(new UiTextRun { Key = "marker", Text = UiText.Of("primary") }, mock);
		}
	}

	private static class DisposalOtherView
	{
		[UiPreview("Default")]
		public static UiTextRun Default() => new()
			{ Key = "marker", Text = UiText.Of("other") };
	}

	[Test]
	public async Task Opening_a_different_preview_disposes_the_first_ones_mock()
	{
		DisposalPrimaryView.Created = [];
		var primaryId = FindId(nameof(DisposalPrimaryView), "Default");
		var otherId = FindId(nameof(DisposalOtherView), "Default");

		var first = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = primaryId }, DeviceA, isAdmin: true);
		await first.Ready;
		var mock = DisposalPrimaryView.Created[0];

		await Broker.CloseAsync(first.SessionId, "the panel selection changed", CancellationToken.None);
		var second = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = otherId }, DeviceA, isAdmin: true);
		await second.Ready;

		await WaitForAsync(() => mock.Disposed, "Switching previews never disposed the first scenario's mock.");
	}

	[Test]
	public async Task Refreshing_the_same_preview_disposes_the_first_ones_mock()
	{
		DisposalPrimaryView.Created = [];
		var previewId = FindId(nameof(DisposalPrimaryView), "Default");
		var request = new OpenUiPreviewSessionRequest { PreviewId = previewId };

		var first = _opener.Open(request, DeviceA, isAdmin: true);
		await first.Ready;
		var mock = DisposalPrimaryView.Created[0];

		var second = _opener.Open(request, DeviceA, isAdmin: true);
		await second.Ready;

		await WaitForAsync(() => mock.Disposed, "Refreshing never disposed the previous scenario's mock.");
	}

	[Test]
	public async Task Closing_a_preview_disposes_its_mock_and_leaves_none_live()
	{
		DisposalPrimaryView.Created = [];
		var previewId = FindId(nameof(DisposalPrimaryView), "Default");

		var ticket = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = previewId }, DeviceA, isAdmin: true);
		await ticket.Ready;
		var mock = DisposalPrimaryView.Created[0];

		await Broker.CloseAsync(ticket.SessionId, "closed", CancellationToken.None);
		await WaitForAsync(() => mock.Disposed, "Closing the preview never disposed the scenario's mock.");

		Assert.That(DisposalPrimaryView.Created.Count(created => !created.Disposed), Is.EqualTo(0));
	}

	[Test]
	public async Task A_preview_serves_one_client_at_a_time()
	{
		var previewId = FindId(nameof(MarkerScenarioView), "Default");

		var ticket = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = previewId }, DeviceA, isAdmin: true);
		await ticket.Ready;

		await TreeJsonAsync(ticket.SessionId, "c1");
		var second = Attach(ticket.SessionId, "c2");

		Assert.Multiple(() =>
		{
			Assert.That(second.Accepted, Is.False);
			Assert.That(second.Code, Is.EqualTo(UiSessionErrorCodes.SessionBusy));
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c2"), Is.Empty);
		});
	}

	private static class FaultyView
	{
		[UiPreview("Default")]
		public static UiTextRun Default() => new()
			{ Key = "marker", Text = UiText.Of("ok") };

		[UiPreview("Boom")]
		public static UiElement Boom() => throw new InvalidOperationException("boom");

		[UiPreview("Bad")]
		public static UiTextRun Bad(string ignored) =>
			new()
				{ Key = "marker", Text = UiText.Of(ignored) };
	}

	[Test]
	public async Task A_throwing_or_malformed_preview_costs_only_itself()
	{
		var result = UiPreviewCatalog.Scan(typeof(UiPreviewSessionTests).Assembly);
		var relevant = result.Registrations.Where(registration =>
			string.Equals(registration.Declaration.View, nameof(FaultyView), StringComparison.Ordinal));

		Assert.That(relevant.Select(registration => registration.Declaration.Scenario),
			Is.EquivalentTo(_faultyScenarioNames));

		var defaultId = FindId(nameof(FaultyView), "Default");
		var boomId = FindId(nameof(FaultyView), "Boom");

		var opened = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = defaultId }, DeviceA, isAdmin: true);
		Assert.That(opened.Accepted, Is.True);
		await opened.Ready;
		await Broker.CloseAsync(opened.SessionId, "done", CancellationToken.None);

		var boomTicket = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = boomId }, DeviceA, isAdmin: true);
		var boomReady = await boomTicket.Ready;
		Assert.That(boomReady.Accepted, Is.False, "The throwing scenario should not have succeeded.");

		var boomProviderId = UiPreviewProviderRegistry.ProviderIdFor(boomId, DeviceA);
		var liveBoomSessions = Registry.SessionsForProvider(boomProviderId)
			.Where(session => session.State is not (UiSessionState.Closed or UiSessionState.Invalidated));
		Assert.That(liveBoomSessions, Is.Empty, "The failed preview left a live orphan session in the registry.");

		var openedAgain = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = defaultId },
			DeviceA,
			isAdmin: true);
		Assert.That(openedAgain.Accepted, Is.True);
		await openedAgain.Ready;
	}

	private sealed class RecordingUiProvider : IIntegration, IUiProvider
	{
		public string Id => "com.example.recording";

		public LocalizedText Name => Id;

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions => [];

		public bool IsInitialized => true;

		public List<UiSessionRequest> Requests { get; } = [];

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
		[
			new()
				{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
			new()
				{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			new()
				{ Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
			new()
				{ Kind = UiSurfaceKinds.Folder, SessionMode = UiSessionModes.Exclusive },
		];

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult<IUiSession?>(new StubUiSession
				{ Tree = () => TreeAt(1, request.Surface.SessionMode) });
		}
	}

	[Test]
	public async Task Opening_a_developer_preview_never_reaches_a_production_provider()
	{
		var provider = new RecordingUiProvider();
		Integrations.Add(provider);

		var previewId = FindId(nameof(MarkerScenarioView), "Default");
		var previewTicket = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = previewId },
			DeviceA,
			isAdmin: true);
		Assert.That(previewTicket.Accepted, Is.True);
		await previewTicket.Ready;

		Assert.That(provider.Requests, Is.Empty, "The developer-preview surface reached a production provider.");

		var configTicket = Broker.Open(provider.Id, Surface(UiSessionModes.Exclusive, UiSurfaceKinds.Config), DeviceA);
		await configTicket.Ready;

		Assert.Multiple(() =>
		{
			Assert.That(provider.Requests, Has.Count.EqualTo(1));
			Assert.That(provider.Requests[0].Surface.Kind, Is.EqualTo(UiSurfaceKinds.Config));
		});
	}

	private static class AdminGatedView
	{
		public static int Invocations;

		[UiPreview("Default")]
		public static UiTextRun Default()
		{
			Invocations++;
			return new UiTextRun { Key = "marker", Text = UiText.Of("x") };
		}
	}

	[Test]
	public async Task A_non_admin_open_is_rejected_before_the_scenario_runs_and_an_admin_open_succeeds()
	{
		AdminGatedView.Invocations = 0;
		var previewId = FindId(nameof(AdminGatedView), "Default");

		var rejected = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = previewId }, DeviceA, isAdmin: false);

		Assert.Multiple(() =>
		{
			Assert.That(rejected.Accepted, Is.False);
			Assert.That(rejected.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(AdminGatedView.Invocations, Is.Zero, "The scenario ran before the admin check.");
		});

		var accepted = _opener.Open(new OpenUiPreviewSessionRequest { PreviewId = previewId }, DeviceA, isAdmin: true);
		Assert.That(accepted.Accepted, Is.True);
		await accepted.Ready;
		Assert.That(AdminGatedView.Invocations, Is.EqualTo(1));
	}

	private sealed class DelegatingResolver : IUiSessionProviderResolver
	{
		private readonly Func<string, IUiSessionProvider?> _resolve;

		public DelegatingResolver(Func<string, IUiSessionProvider?> resolve) => _resolve = resolve;

		public IUiSessionProvider? Resolve(string providerId) => _resolve(providerId);
	}

	/// <summary>A source scanning the test assembly itself, so this file's own <c>[UiPreview]</c>-attributed
	/// scenarios are what it declares - the same shape <c>WidgetUiPreviewSource</c> has for the real
	/// widget assembly.</summary>
	private sealed class TestUiPreviewSource : IUiPreviewSource
	{
		private readonly Lazy<UiPreviewScanResult> _scan;

		public TestUiPreviewSource(Assembly assembly)
			=> _scan = new Lazy<UiPreviewScanResult>(() => UiPreviewCatalog.Scan(assembly));

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
		[
			new()
				{ Kind = UiSurfaceKinds.DeveloperPreview, SessionMode = UiSessionModes.Exclusive }
		];

		public IReadOnlyList<UiPreviewDescriptor> Previews =>
		[
			.. _scan.Value.Registrations.Select(registration => new UiPreviewDescriptor
			{
				Id = registration.Declaration.Id,
				View = registration.Declaration.View,
				Scenario = registration.Declaration.Scenario,
				Profile = registration.Declaration.Profile,
			}),
		];

		public IReadOnlyList<UiPreviewSkipped> Skipped =>
		[
			.. _scan.Value.Diagnostics.Select(diagnostic => new UiPreviewSkipped
			{
				Member = diagnostic.Member, Reason = diagnostic.Reason,
			}),
		];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			if (request.Surface.Kind != UiSurfaceKinds.DeveloperPreview ||
				!request.Surface.Attributes.TryGetValue(UiDeveloperPreviewSurfaceAttributes.PreviewId,
					out var idElement) ||
				idElement.ValueKind != JsonValueKind.String)
			{
				return Task.FromResult<IUiSession?>(null);
			}

			var previewId = idElement.GetString();
			var match = _scan.Value.Registrations.FirstOrDefault(registration =>
				string.Equals(registration.Declaration.Id, previewId, StringComparison.Ordinal));

			return match is null
				? Task.FromResult<IUiSession?>(null)
				: Task.FromResult<IUiSession?>(new WidgetUiPreviewSession(match.Create(request.Surface)));
		}
	}
}
