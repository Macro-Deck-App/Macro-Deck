using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Lock;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class MacOsLockStateWatcherTests
{
	[Test]
	public void Constructs_with_native_support()
	{
		using var watcher = new MacOsLockStateWatcher();

		Assert.That(watcher.IsSupported, Is.True);
	}

	[Test]
	public async Task WatchAsync_ends_without_faulting_when_cancelled()
	{
		using var watcher = new MacOsLockStateWatcher();
		using var cts = new CancellationTokenSource();

		var enumeration = Task.Run(async () =>
		{
			await foreach (var _ in watcher.WatchAsync(cts.Token))
			{
			}
		});

		await cts.CancelAsync();

		Exception? unexpected = null;
		try
		{
			await enumeration.WaitAsync(TimeSpan.FromSeconds(5));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			unexpected = ex;
		}

		Assert.Multiple(() =>
		{
			Assert.That(unexpected, Is.Null);
			Assert.DoesNotThrow(watcher.Dispose);
		});
	}

	[Test]
	public void Constructing_disposing_and_constructing_a_second_instance_does_not_throw()
	{
		Assert.DoesNotThrow(() =>
		{
			var first = new MacOsLockStateWatcher();
			first.Dispose();

			using var second = new MacOsLockStateWatcher();
			Assert.That(second.IsSupported, Is.True);
		});
	}
}
