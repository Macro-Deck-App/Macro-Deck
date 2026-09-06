using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetDataWriteLockTests
{
	[Test]
	public async Task ASecondWriterOnTheSameWidget_WaitsForTheFirst()
	{
		var writeLock = new WidgetDataWriteLock();
		var widgetId = Guid.NewGuid();

		var first = await writeLock.AcquireAsync(widgetId);
		var second = writeLock.AcquireAsync(widgetId);

		Assert.That(second.IsCompleted, Is.False);

		first.Dispose();
		(await second).Dispose();
	}

	[Test]
	public async Task WritersOnDifferentWidgets_DoNotWaitForEachOther()
	{
		var writeLock = new WidgetDataWriteLock();

		var first = await writeLock.AcquireAsync(Guid.NewGuid());
		var second = writeLock.AcquireAsync(Guid.NewGuid());

		Assert.That(second.IsCompleted, Is.True);

		first.Dispose();
		(await second).Dispose();
	}

	[Test]
	public async Task DisposingTwice_ReleasesOnce()
	{
		var writeLock = new WidgetDataWriteLock();
		var widgetId = Guid.NewGuid();

		var first = await writeLock.AcquireAsync(widgetId);
		first.Dispose();
		first.Dispose();

		var second = await writeLock.AcquireAsync(widgetId);
		var third = writeLock.AcquireAsync(widgetId);

		Assert.That(third.IsCompleted, Is.False);

		second.Dispose();
		(await third).Dispose();
	}
}
