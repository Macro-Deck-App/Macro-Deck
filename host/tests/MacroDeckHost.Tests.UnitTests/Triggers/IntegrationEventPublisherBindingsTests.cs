using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class IntegrationEventPublisherBindingsTests
{
	private EventSubscriptionIndex _index = null!;
	private EventBindingTracker _tracker = null!;
	private IntegrationEventPublisher _publisher = null!;

	[SetUp]
	public void SetUp()
	{
		_index = new EventSubscriptionIndex(new StubFolderCache(), new StubAutomationCache());
		_tracker = new EventBindingTracker(_index, Serilog.Core.Logger.None);
		_publisher = new IntegrationEventPublisher("com.hotkeys", new RecordingEventBus(), _tracker, Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown() => _tracker.Dispose();

	[Test]
	public async Task BindingsChanged_fires_for_the_integrations_own_events_only()
	{
		using var raised = new SemaphoreSlim(0);
		_publisher.BindingsChanged += () => raised.Release();

		_index.ReindexAutomation(Guid.NewGuid(),
			EventBindingTrackerTests.Flows("com.other", "scene-changed", EventBindingTrackerTests.Combo("F4")),
			enabled: true);
		await _tracker.FlushAsync();
		var raisedForOther = await raised.WaitAsync(TimeSpan.FromMilliseconds(200));

		_index.ReindexAutomation(Guid.NewGuid(),
			EventBindingTrackerTests.Flows("com.hotkeys", "hotkey-pressed", EventBindingTrackerTests.Combo("F3")),
			enabled: true);
		await _tracker.FlushAsync();
		var raisedForOwn = await raised.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(raisedForOther, Is.False);
			Assert.That(raisedForOwn, Is.True);
			Assert.That(_publisher.GetBindings().Single().EventId, Is.EqualTo("hotkey-pressed"));
		});
	}

	[Test]
	public async Task A_blocking_BindingsChanged_handler_holds_up_neither_the_index_writer_nor_delivery()
	{
		using var release = new ManualResetEventSlim();
		_publisher.BindingsChanged += () => release.Wait(TimeSpan.FromSeconds(10));

		var write = Task.Run(() => _index.ReindexAutomation(Guid.NewGuid(),
			EventBindingTrackerTests.Flows("com.hotkeys", "hotkey-pressed", EventBindingTrackerTests.Combo("F3")),
			enabled: true));
		var writerReturned = await Task.WhenAny(write, Task.Delay(TimeSpan.FromSeconds(5))) == write;
		var flush = _tracker.FlushAsync();
		var deliveryFinished = await Task.WhenAny(flush, Task.Delay(TimeSpan.FromSeconds(5))) == flush;
		release.Set();

		Assert.Multiple(() =>
		{
			Assert.That(writerReturned, Is.True);
			Assert.That(deliveryFinished, Is.True);
		});
	}

	[Test]
	public void The_publish_only_constructor_reports_nothing_bound()
	{
		var publisher = new IntegrationEventPublisher("com.hotkeys", new RecordingEventBus(), Serilog.Core.Logger.None);

		Assert.That(publisher.GetBindings(), Is.Empty);
	}
}
