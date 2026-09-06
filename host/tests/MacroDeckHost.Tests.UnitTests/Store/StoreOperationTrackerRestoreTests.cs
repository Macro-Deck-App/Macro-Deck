using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>An operation the host was running when it stopped cannot be resumed - the process that owned
/// it is gone - so on the next start it must come back as a retryable failure rather than silently
/// vanish or appear to still be in progress forever.</summary>
[TestFixture]
internal sealed class StoreOperationTrackerRestoreTests
{
	private TestPaths _paths = null!;
	private JsonStoreOperationStore _store = null!;
	private ManualTimeProvider _time = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		_store = new JsonStoreOperationStore(_paths, Serilog.Core.Logger.None);
		_time = new ManualTimeProvider();
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void An_operation_left_downloading_in_the_persisted_store_comes_back_as_failed_interrupted_and_retryable()
	{
		var startedAt = _time.Now;
		var interrupted = new StoreOperation
		{
			Id = Guid.CreateVersion7(),
			Kind = StoreOperationKind.Install,
			ExtensionKind = StoreExtensionKind.Plugin,
			PackageId = "com.acme.hue",
			Version = "1.0.0",
			DisplayName = "Hue Bridge",
			State = StoreOperationState.Downloading,
			BytesDownloaded = 512,
			StartedAt = startedAt,
			UpdatedAt = startedAt
		};
		_store.SaveAll([interrupted]);

		// Simulates the host restarting: a fresh tracker over the same persisted store, exactly what
		// StoreOperationBackgroundService does on startup.
		var tracker = new StoreOperationTracker(_store, _time);
		tracker.Restore(_store.LoadAll());

		var restored = tracker.Find(interrupted.Id);
		Assert.Multiple(() =>
		{
			Assert.That(restored, Is.Not.Null);
			Assert.That(restored!.State, Is.EqualTo(StoreOperationState.Failed));
			Assert.That(restored.Error, Is.EqualTo(StoreOperationError.Interrupted));
			Assert.That(restored.CanRetry, Is.True);
		});
	}

	[Test]
	public void A_restored_terminal_operation_is_left_exactly_as_it_was()
	{
		var completed = new StoreOperation
		{
			Id = Guid.CreateVersion7(),
			Kind = StoreOperationKind.Install,
			ExtensionKind = StoreExtensionKind.IconPack,
			PackageId = "com.acme.material",
			Version = "1.0.0",
			DisplayName = "Material Icons",
			State = StoreOperationState.Completed,
			StartedAt = _time.Now,
			UpdatedAt = _time.Now,
			CompletedAt = _time.Now
		};
		_store.SaveAll([completed]);

		var tracker = new StoreOperationTracker(_store, _time);
		tracker.Restore(_store.LoadAll());

		var restored = tracker.Find(completed.Id);
		Assert.That(restored!.State, Is.EqualTo(StoreOperationState.Completed));
	}
}
