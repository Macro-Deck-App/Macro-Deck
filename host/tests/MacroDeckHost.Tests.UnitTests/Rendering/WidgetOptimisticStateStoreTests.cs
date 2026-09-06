using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Rendering;

[TestFixture]
public sealed class WidgetOptimisticStateStoreTests
{
	[Test]
	public void A_later_started_execution_prevents_an_older_completion_from_replacing_it()
	{
		var store = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = new WidgetOptimisticStateIdentity(Guid.NewGuid(), "provider", "system", "mute-volume");
		var older = store.Begin(identity);
		var newer = store.Begin(identity);

		Assert.Multiple(() =>
		{
			Assert.That(store.TryApply(identity, older, "unmuted"), Is.False);
			Assert.That(store.TryApply(identity, newer, "muted"), Is.True);
			Assert.That(store.Get(identity)!.ExpectedStateId, Is.EqualTo("muted"));
		});
	}

	[Test]
	public void ClearingAWidget_NeverReusesAGenerationHeldByAnInFlightAction()
	{
		var store = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = new WidgetOptimisticStateIdentity(Guid.NewGuid(), "provider", "system", "mute-volume");
		var beforeClear = store.Begin(identity);

		store.ClearWidget(identity.WidgetId);
		var afterClear = store.Begin(identity);

		Assert.Multiple(() =>
		{
			Assert.That(afterClear, Is.GreaterThan(beforeClear));
			Assert.That(store.TryApply(identity, beforeClear, "unmuted"), Is.False);
			Assert.That(store.TryApply(identity, afterClear, "muted"), Is.True);
		});
	}

	[Test]
	public async Task ExpirationAtFiveSeconds_QueuesTheOwningWidgetImmediately()
	{
		var time = new FakeTimeProvider();
		var queue = new WidgetStateEvalChannel();
		var store = new WidgetOptimisticStateStore(time, queue);
		var identity = new WidgetOptimisticStateIdentity(Guid.NewGuid(), "provider", "system", "mute-volume");
		store.TryApply(identity, store.Begin(identity), "muted");

		time.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
		Assert.That(queue.Reader.TryRead(out _), Is.False);

		time.Advance(TimeSpan.FromTicks(1));
		await Task.Yield();

		Assert.Multiple(() =>
		{
			Assert.That(store.Get(identity), Is.Null);
			Assert.That(queue.Reader.TryRead(out var widgetId), Is.True);
			Assert.That(widgetId, Is.EqualTo(identity.WidgetId));
		});
	}

	[Test]
	public void NewerEntryBetweenReadAndConfirmation_IsReturnedAsOneAtomicSnapshot()
	{
		var store = new WidgetOptimisticStateStore(TimeProvider.System);
		var identity = new WidgetOptimisticStateIdentity(Guid.NewGuid(), "provider", "teams", "toggle");
		var olderGeneration = store.Begin(identity);
		store.TryApply(identity, olderGeneration, "on");
		var olderSnapshot = store.Read(identity);
		var newerGeneration = store.Begin(identity);
		store.TryApply(identity, newerGeneration, "off");

		var afterConfirmation = store.Confirm(identity, olderSnapshot.State!.Generation, "on");

		Assert.Multiple(() =>
		{
			Assert.That(afterConfirmation.State?.ExpectedStateId, Is.EqualTo("off"));
			Assert.That(afterConfirmation.State?.Generation, Is.EqualTo(newerGeneration));
			Assert.That(store.IsCurrent(afterConfirmation.Version), Is.True);
		});
	}
}
