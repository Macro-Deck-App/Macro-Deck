using System.Runtime.CompilerServices;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Infrastructure.Deck;
using MacroDeckHost.Integrations.System.Focus;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
public class FocusedApplicationWatcherTests
{
	[TearDown]
	public void TearDown() => FocusedApplicationSnapshot.Current.Set(null);

	[Test]
	public async Task WatchAsync_MapsEveryFieldFromTheInnerWatcher()
	{
		var inner = new FakeFocusedApplicationWatcher([
			new FocusedAppInfo(4242, "/usr/bin/foo", "procfoo", "com.example.foo"),
			new FocusedAppInfo(7, null, null, null)
		]);
		var watcher = new FocusedApplicationWatcher(inner);

		var results = new List<FocusedApplication>();
		await foreach (var app in watcher.WatchAsync(CancellationToken.None))
		{
			results.Add(app);
		}

		Assert.That(results, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(results[0].ProcessId, Is.EqualTo(4242));
			Assert.That(results[0].ExecutablePath, Is.EqualTo("/usr/bin/foo"));
			Assert.That(results[0].ProcessName, Is.EqualTo("procfoo"));
			Assert.That(results[0].BundleId, Is.EqualTo("com.example.foo"));

			Assert.That(results[1].ProcessId, Is.EqualTo(7));
			Assert.That(results[1].ExecutablePath, Is.Null);
			Assert.That(results[1].ProcessName, Is.Null);
			Assert.That(results[1].BundleId, Is.Null);
		});
	}

	[Test]
	public async Task WatchAsync_UpdatesTheProcessWideSnapshotAsItYields()
	{
		var inner = new FakeFocusedApplicationWatcher([
			new FocusedAppInfo(4242, "/usr/bin/foo", "procfoo", "com.example.foo")
		]);
		var watcher = new FocusedApplicationWatcher(inner);

		await foreach (var _ in watcher.WatchAsync(CancellationToken.None))
		{
		}

		Assert.That(FocusedApplicationSnapshot.Current.Value,
			Is.EqualTo(new FocusedAppInfo(4242, "/usr/bin/foo", "procfoo", "com.example.foo")));
	}

	[Test]
	public void Dispose_DelegatesToTheInnerWatcher()
	{
		var inner = new FakeFocusedApplicationWatcher([]);
		var watcher = new FocusedApplicationWatcher(inner);

		watcher.Dispose();

		Assert.That(inner.Disposed, Is.True);
	}

	private sealed class FakeFocusedApplicationWatcher(IReadOnlyList<FocusedAppInfo> items) : IFocusedApplicationWatcher
	{
		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public bool Disposed { get; private set; }

		public async IAsyncEnumerable<FocusedAppInfo> WatchAsync(
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			foreach (var item in items)
			{
				yield return item;
				await Task.Yield();
			}
		}

		public void Dispose() => Disposed = true;
	}
}
