using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Ui.Modals;

/// <summary>
/// The promise an action relies on when it awaits a modal (issue #785): completion and cancellation are
/// distinguishable, and every way of not answering settles as a cancellation, so the wait cannot hang.
/// </summary>
[TestFixture]
public class ModalInteractionCoordinatorTests
{
	private const string Principal = "device-a";
	private const string OtherPrincipal = "device-b";
	private const string ClientId = "client-a";
	private const string IntegrationId = "app.macro-deck.weather";

	private ManualTimeProvider _time = null!;
	private ModalInteractionCoordinator _coordinator = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new ManualTimeProvider();
		_coordinator = new ModalInteractionCoordinator(_time);
	}

	[Test]
	public void AModalWithNoClientToShowItOn_IsRefused()
		=> Assert.That(_coordinator.Register(IntegrationId, originClientId: null, Definition()), Is.Null);

	[Test]
	public void AModalNamingNoView_IsRefused()
		=> Assert.That(_coordinator.Register(IntegrationId, ClientId, new ModalDefinition { ViewId = string.Empty }),
			Is.Null);

	/// <summary>A dialog interrupts the user, so several stacked on one client is a bug to refuse rather
	/// than a workload to serve.</summary>
	[Test]
	public void OneClient_CannotBeGivenUnboundedModals()
	{
		for (var i = 0; i < 3; i++)
		{
			Assert.That(_coordinator.Register(IntegrationId, ClientId, Definition()), Is.Not.Null);
		}

		Assert.That(_coordinator.Register(IntegrationId, ClientId, Definition()), Is.Null);
	}

	[Test]
	public void TheCapIsPerClient_NotGlobal()
	{
		for (var i = 0; i < 3; i++)
		{
			_coordinator.Register(IntegrationId, ClientId, Definition());
		}

		Assert.That(_coordinator.Register(IntegrationId, "client-b", Definition()), Is.Not.Null);
	}

	[Test]
	public async Task Completing_HandsTheValueToTheWaitingAction()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		var waiting = _coordinator.AwaitAsync(modalId, CancellationToken.None);
		_coordinator.TryClaim(modalId, Principal, out _);

		Assert.That(_coordinator.Settle(modalId, Principal, cancelled: false, Value("device-1")), Is.True);

		var result = await waiting;
		Assert.That(result.Cancelled, Is.False);
		Assert.That(result.Value.GetProperty("deviceId").GetString(), Is.EqualTo("device-1"));
	}

	[Test]
	public async Task Cancelling_ReachesTheWaitingActionAsACancellation()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		var waiting = _coordinator.AwaitAsync(modalId, CancellationToken.None);
		_coordinator.TryClaim(modalId, Principal, out _);

		_coordinator.Settle(modalId, Principal, cancelled: true, value: null);

		Assert.That((await waiting).Cancelled, Is.True);
	}

	/// <summary>Reading a value the user did not supply is the mistake this contract exists to prevent,
	/// so a missing value is a cancellation whatever the flag says.</summary>
	[Test]
	public async Task ACompletionWithNoValue_IsACancellation()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		var waiting = _coordinator.AwaitAsync(modalId, CancellationToken.None);
		_coordinator.TryClaim(modalId, Principal, out _);

		_coordinator.Settle(modalId, Principal, cancelled: false, value: null);

		Assert.That((await waiting).Cancelled, Is.True);
	}

	[Test]
	public async Task TheFlowBeingCancelled_SettlesTheWaitAsACancellation()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		using var cts = new CancellationTokenSource();

		var waiting = _coordinator.AwaitAsync(modalId, cts.Token);
		await cts.CancelAsync();

		Assert.That((await waiting).Cancelled, Is.True);
	}

	[Test]
	public async Task TheSessionEnding_SettlesTheWaitAsACancellation()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		var waiting = _coordinator.AwaitAsync(modalId, CancellationToken.None);
		_coordinator.TryClaim(modalId, Principal, out _);
		_coordinator.BindSession(modalId, "session-1");

		_coordinator.CancelForSession("session-1");

		Assert.That((await waiting).Cancelled, Is.True);
	}

	[Test]
	public async Task TheClientGoingAway_SettlesTheWaitAsACancellation()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		var waiting = _coordinator.AwaitAsync(modalId, CancellationToken.None);

		_coordinator.CancelForClient(ClientId);

		Assert.That((await waiting).Cancelled, Is.True);
	}

	/// <summary>
	/// Nothing else removes a fire-and-forget modal whose client never opened it, so without the sweep it
	/// would sit in the map for the life of the process - and go on counting against that client's cap.
	/// An hour is chosen as comfortably past the host's maximum flow run rather than to match the sweep's
	/// own window.
	/// </summary>
	[Test]
	public void AModalNobodyCouldStillBeWaitingOn_IsForgotten()
	{
		var accepted = FillUntilRefused(ClientId);

		_time.Advance(TimeSpan.FromHours(1));
		_coordinator.SweepExpired();

		Assert.That(FillUntilRefused(ClientId), Is.EqualTo(accepted), "the forgotten modals still count");
	}

	[Test]
	public void AModalIsClaimedByTheFirstPrincipalToOpenIt()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;

		Assert.That(_coordinator.TryClaim(modalId, Principal, out var claimed), Is.True);
		Assert.That(claimed.ViewId, Is.EqualTo("weather-details"));
		Assert.That(_coordinator.TryClaim(modalId, OtherPrincipal, out _), Is.False);
	}

	/// <summary>One device must not be able to answer another device's dialog.</summary>
	[Test]
	public void AnotherPrincipal_CannotSettleTheModal()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;
		_coordinator.TryClaim(modalId, Principal, out _);

		Assert.That(_coordinator.Settle(modalId, OtherPrincipal, cancelled: false, Value("device-1")), Is.False);
	}

	[Test]
	public void AnUnclaimedModal_CannotBeSettled()
	{
		var modalId = _coordinator.Register(IntegrationId, ClientId, Definition())!;

		Assert.That(_coordinator.Settle(modalId, Principal, cancelled: true, value: null), Is.False);
	}

	[Test]
	public async Task AwaitingAModalThatWasNeverRegistered_IsACancellationRatherThanAHang()
		=> Assert.That((await _coordinator.AwaitAsync("nope", CancellationToken.None)).Cancelled, Is.True);

	/// <summary>Far past any cap a dialog surface could sensibly have, so the loop below measures the cap
	/// rather than restating it.</summary>
	private const int _wellPastAnySaneCap = 100;

	/// <summary>Registers modals for one client until it is refused. Returns how many were accepted.</summary>
	private int FillUntilRefused(string clientId)
	{
		var accepted = 0;

		while (accepted < _wellPastAnySaneCap &&
			_coordinator.Register(IntegrationId, clientId, Definition()) is not null)
		{
			accepted++;
		}

		return accepted;
	}

	private static ModalDefinition Definition() => new() { ViewId = "weather-details" };

	private static JsonElement Value(string deviceId)
		=> JsonSerializer.SerializeToElement(new { deviceId });
}
