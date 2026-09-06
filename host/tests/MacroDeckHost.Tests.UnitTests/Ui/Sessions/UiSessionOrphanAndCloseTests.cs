using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Configuration surfaces open sessions from UI interaction - a dialog opening, a card expanding - so
/// a session that is never attached to, or one whose surface is closed again straight away, is ordinary
/// rather than exceptional. A provider is allowed only
/// <see cref="ProtocolLimits.MaxUiSessionsPerProvider" /> sessions, so in both cases the slot has to
/// come back or the next surface silently falls back to the legacy field list.
/// </summary>
[TestFixture]
internal sealed class UiSessionOrphanAndCloseTests : UiSessionFixture
{
	[Test]
	public async Task A_session_no_client_ever_attached_to_does_not_hold_its_slot_forever()
	{
		var provider = AddProvider();

		// Fill the provider's whole quota with sessions that open and are then abandoned before any
		// client attaches - a reload, a navigation, a dropped frame between the open and the attach.
		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider; index++)
		{
			await OpenAsync(provider);
		}

		var whileFull = Broker.Open(provider.ProviderId, Surface(), DeviceA);

		Time.Advance(TimeSpan.FromMinutes(1));
		await SettleAsync();

		var afterGrace = Broker.Open(provider.ProviderId, Surface(), DeviceA);

		Assert.Multiple(() =>
		{
			Assert.That(whileFull.Accepted, Is.False);
			Assert.That(whileFull.Code, Is.EqualTo(UiSessionErrorCodes.TooManySessions));

			// The real assertion: expiring the records without giving the quota back would turn an
			// orphaned session into a denial of service against the provider.
			Assert.That(afterGrace.Accepted,
				Is.True,
				"Orphaned sessions expired without returning the provider's session slots.");
		});
	}

	[Test]
	public async Task An_attached_session_is_not_treated_as_an_orphan()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		Time.Advance(TimeSpan.FromMinutes(1));
		await SettleAsync();

		Assert.That(Registry.Find(sessionId),
			Is.Not.Null,
			"A session with a client attached was collected as if nobody had ever attached.");
	}

	// The clock is deliberately never advanced in this test. Closing by detaching alone would leave the
	// session draining behind a timer that a frozen clock never fires, so the ninth open would trip the
	// per-provider limit - which is exactly the regression this pins.
	[Test]
	public async Task Closing_a_surface_returns_its_slot_without_waiting_out_the_drain_grace()
	{
		var provider = AddProvider();

		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider * 2; index++)
		{
			var sessionId = await OpenAsync(provider);
			Attach(sessionId, "c1");

			Assert.That(Broker.CloseOwned(sessionId, DeviceA, "the surface was closed"),
				Is.True,
				$"Closing session {index + 1} was refused.");
		}

		var reopened = Broker.Open(provider.ProviderId, Surface(), DeviceA);

		Assert.That(reopened.Accepted,
			Is.True,
			"Closed sessions kept their slots, so opening and closing a surface repeatedly exhausts the provider.");
	}

	[Test]
	public async Task A_session_is_only_closable_by_the_principal_that_owns_it()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, principal: DeviceA);
		Attach(sessionId, "c1", DeviceA);

		var byStranger = Broker.CloseOwned(sessionId, DeviceB, "not mine to close");

		Assert.Multiple(() =>
		{
			Assert.That(byStranger, Is.False);
			Assert.That(Attach(sessionId, "c1", DeviceA).Accepted,
				Is.True,
				"A session was ended by a principal that does not own it.");
		});
	}
}
