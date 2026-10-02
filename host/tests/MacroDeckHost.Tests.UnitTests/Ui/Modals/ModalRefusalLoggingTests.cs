using System.Globalization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Ui.Modals;

[TestFixture]
public class ModalRefusalLoggingTests
{
	private const string Principal = "device-a";
	private const string ClientId = "client-a";
	private const string IntegrationId = "app.macro-deck.timers";
	private const string ViewId = "countdown-duration";

	private ModalInteractionCoordinator _coordinator = null!;
	private List<LogEvent> _events = null!;
	private Serilog.Core.Logger _logger = null!;

	[SetUp]
	public void SetUp()
	{
		_coordinator = new ModalInteractionCoordinator(new ManualTimeProvider());
		_events = [];
		_logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(new DelegatingLogSink(_events.Add))
			.CreateLogger();
	}

	[TearDown]
	public void TearDown() => _logger.Dispose();

	[Test]
	public void OpeningAModalTheProviderCannotServe_LogsTheModalIntegrationViewAndCode()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, new ModalDefinition { ViewId = ViewId })!;
		var opener = new ModalUiSessionOpener(_coordinator, new RecordingUiSessionBroker(), _logger);

		var response = opener.Open(new OpenModalUiSessionRequest { ModalId = modalId }, Principal);

		Assert.That(response.Accepted, Is.False);
		var logged = Rendered(LogEventLevel.Warning);
		Assert.That(logged, Has.Some.Contains(modalId)
			.And.Some.Contains(IntegrationId)
			.And.Some.Contains(ViewId)
			.And.Some.Contains(response.Code));
	}

	[Test]
	public void OpeningAModalThatIsGone_LogsTheModalAndCode_AndAnswersAsBefore()
	{
		var opener = new ModalUiSessionOpener(_coordinator, new RecordingUiSessionBroker(), _logger);

		var response = opener.Open(new OpenModalUiSessionRequest { ModalId = "gone" }, Principal);

		Assert.That(response.Accepted, Is.False);
		Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		Assert.That(Rendered(LogEventLevel.Information),
			Has.Some.Contains("gone").And.Some.Contains(UiSessionErrorCodes.SessionNotFound));
	}

	[Test]
	public void OpeningAModalAnotherClientClaimed_LogsItsIntegrationAndView_AndAnswersLikeAMissingOne()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, new ModalDefinition { ViewId = ViewId })!;
		_coordinator.TryClaim(modalId, "device-b", out _);
		var opener = new ModalUiSessionOpener(_coordinator, new RecordingUiSessionBroker(), _logger);

		var response = opener.Open(new OpenModalUiSessionRequest { ModalId = modalId }, Principal);

		Assert.That(response.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		Assert.That(response.Message, Is.EqualTo(opener.Open(new OpenModalUiSessionRequest { ModalId = "gone" }, Principal).Message));
		Assert.That(Rendered(LogEventLevel.Information), Has.Some.Contains(IntegrationId).And.Some.Contains(ViewId));
	}

	[Test]
	public async Task ASessionEndingUnderAnOpenModal_LogsWhichModalItCancelledAndWhy()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, new ModalDefinition { ViewId = ViewId })!;
		var waiting = _coordinator.AwaitAsync(modalId, CancellationToken.None);
		_coordinator.TryClaim(modalId, Principal, out _);
		_coordinator.BindSession(modalId, "session-1");
		var watcher = Watcher();

		watcher.OnSessionEnded(null, Ended("session-1", "PROVIDER_TIMEOUT", retryable: true));

		Assert.That((await waiting).Cancelled, Is.True);
		Assert.That(Rendered(LogEventLevel.Information), Has.Some.Contains(modalId)
			.And.Some.Contains(IntegrationId)
			.And.Some.Contains(ViewId)
			.And.Some.Contains("PROVIDER_TIMEOUT"));
	}

	[Test]
	public void AClientClosingItsDialogSession_IsNotReportedAsAFailure()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, new ModalDefinition { ViewId = ViewId })!;
		_coordinator.TryClaim(modalId, Principal, out _);
		_coordinator.BindSession(modalId, "session-1");

		Watcher().OnSessionEnded(null, Ended("session-1", code: null, retryable: false, UiSessionEndReason.Closed));

		Assert.That(_events.Where(e => e.Level >= LogEventLevel.Information), Is.Empty);
	}

	[Test]
	public void ASessionEndingWithNoModalOnIt_LogsNothing()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, new ModalDefinition { ViewId = ViewId })!;
		_coordinator.TryClaim(modalId, Principal, out _);
		_coordinator.BindSession(modalId, "session-1");

		Watcher().OnSessionEnded(null, Ended("session-2", "PROVIDER_TIMEOUT", retryable: true));

		Assert.That(_events, Is.Empty);
	}

	[Test]
	public async Task AModalItsCallerWithdraws_IsLoggedWithItsIntegrationAndView()
	{
		var interactions = new UiInteractions(_coordinator, new RecordingUiTransport(), IntegrationId, _logger);
		using var withdraw = new CancellationTokenSource();

		var asking = interactions.ShowModalAsync<int>(ClientId, new ModalDefinition { ViewId = ViewId }, withdraw.Token);
		await withdraw.CancelAsync();

		Assert.That((await asking).Cancelled, Is.True);
		Assert.That(Rendered(LogEventLevel.Information), Has.Some.Contains(IntegrationId).And.Some.Contains(ViewId));
	}

	[Test]
	public async Task AModalTheUserCancels_IsNotReportedAsWithdrawn()
	{
		var transport = new RecordingUiTransport();
		var interactions = new UiInteractions(_coordinator, transport, IntegrationId, _logger);
		var asking = interactions.ShowModalAsync<int>(ClientId, new ModalDefinition { ViewId = ViewId });
		var modalId = ((UiModalOpenedEvent)transport.GroupMessages.Single().Message).ModalId;
		_coordinator.TryClaim(modalId, Principal, out _);

		_coordinator.Settle(modalId, Principal, cancelled: true, value: null);

		Assert.That((await asking).Cancelled, Is.True);
		Assert.That(_events, Is.Empty);
	}

	private ModalSessionWatcher Watcher()
		=> new(new StartedLifetime(), new UiSessionRegistry(TimeProvider.System), _coordinator, _logger);

	private static UiSessionEndedEventArgs Ended(string sessionId,
		string? code,
		bool retryable,
		UiSessionEndReason reason = UiSessionEndReason.Invalidated)
		=> new()
		{
			Session = new UiSessionSnapshot
			{
				SessionId = sessionId,
				ProviderId = IntegrationId,
				Surface = new UiSurface { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive },
				SessionMode = UiSessionModes.Exclusive,
				OwnerPrincipal = Principal,
				State = UiSessionState.Invalidated,
				Revision = 0,
				Attachments = []
			},
			Reason = reason,
			Code = code,
			Retryable = retryable
		};

	private List<string> Rendered(LogEventLevel level)
		=> _events.Where(e => e.Level == level).Select(e => e.RenderMessage(CultureInfo.InvariantCulture)).ToList();

	private sealed class StartedLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
