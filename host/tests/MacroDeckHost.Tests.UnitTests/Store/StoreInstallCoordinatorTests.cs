using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Store;

/// <summary>At most one live operation exists per (kind, packageId): two installs of the same package
/// racing each other (a double click, two browser tabs) must not both try to write the same plugin or
/// icon pack version at once.</summary>
[TestFixture]
internal sealed class StoreInstallCoordinatorTests
{
	private const string PluginId = "com.acme.hue";

	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private JsonStoreInstallationStore _installations = null!;
	private StoreOperationTracker _tracker = null!;
	private StoreOperationChannel _channel = null!;
	private StoreInstallCoordinator _coordinator = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);
		_catalog = new StoreCatalog();
		_installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		_channel = new StoreOperationChannel();

		var plugins = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		var catalogQuery = new StoreCatalogQueryService(_catalog, plugins, _installations);
		_coordinator = new StoreInstallCoordinator(catalogQuery,
			_tracker,
			_channel,
			new StoreOperationCancellation(),
			new StoreInstallConsent());

		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				new StoreCatalogEntry
				{
					Kind = StoreExtensionKind.Plugin,
					Id = PluginId,
					Name = "Hue Bridge",
					LatestVersion = "1.0.0",
					LatestRelease = new StoreReleaseManifest
					{
						Version = "1.0.0",
						ArtifactUrl = new Uri("https://cdn.example/hue.macroDeckPlugin"),
						Sha256 = new string('a', 64),
						Size = 16
					}
				}
			]
		});
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Two_install_calls_for_the_same_package_return_the_same_operation_id()
	{
		var first = _coordinator.Install(StoreExtensionKind.Plugin, PluginId);
		var second = _coordinator.Install(StoreExtensionKind.Plugin, PluginId);

		Assert.That(second.Id, Is.EqualTo(first.Id));
		Assert.That(_tracker.Snapshot(), Has.Count.EqualTo(1));
	}

	[Test]
	public void A_new_install_call_after_the_first_completes_starts_a_second_operation()
	{
		var first = _coordinator.Install(StoreExtensionKind.Plugin, PluginId);
		_tracker.Transition(first.Id, StoreOperationState.Completed);

		var second = _coordinator.Install(StoreExtensionKind.Plugin, PluginId);

		Assert.That(second.Id, Is.Not.EqualTo(first.Id));
	}
}
