using System.Text.Json;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Application.Ui.Transport.Messages.Store;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Infrastructure.Store;
using MacroDeckHost.Tests.UnitTests.Store;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.Api;

[TestFixture]
internal sealed class StoreControllerTests
{
	private const string PluginId = "com.acme.hue";

	private static readonly string[] _appleAndWindowsRids = ["osx-arm64", "osx-x64", "win-x64"];
	private static readonly string[] _expectedOperatingSystems = ["Windows", "macOS"];

	private TestPaths _paths = null!;
	private StoreCatalog _catalog = null!;
	private StoreCatalogQueryService _catalogQuery = null!;
	private StoreOperationTracker _tracker = null!;
	private StoreInstallCoordinator _installCoordinator = null!;
	private FakeStoreUninstallService _uninstallService = null!;
	private StoreController _controller = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.StoreDirectory);
		Directory.CreateDirectory(_paths.PluginsDirectory);
		Directory.CreateDirectory(_paths.StoreMediaDirectory);

		_catalog = new StoreCatalog();
		var installations = new JsonStoreInstallationStore(_paths, Serilog.Core.Logger.None);
		var plugins = new PluginInstallationCatalog(_paths, Serilog.Core.Logger.None);
		_catalogQuery = new StoreCatalogQueryService(_catalog, plugins, installations);
		_tracker = new StoreOperationTracker(new InMemoryStoreOperationStore(), TimeProvider.System);
		_installCoordinator = new StoreInstallCoordinator(_catalogQuery,
			_tracker,
			new StoreOperationChannel(),
			new StoreOperationCancellation(),
			new StoreInstallConsent());

		_controller = new StoreController(_catalogQuery,
			new FakeStoreRegistryRefresher(),
			_installCoordinator,
			_tracker,
			new FakeStoreUpdateDetector(),
			new StoreUpdateState(),
			new ThrowingStoreArtifactDownloader(),
			new FakeDeveloperModePreferences(),
			_paths,
			StoreRegistryOptions.Default,
			new RecordingMediator(),
			_uninstallService = new FakeStoreUninstallService())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
			}
		};
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void An_uninitialised_registry_answers_registry_unavailable_not_an_empty_catalog()
	{
		var response = _controller.GetCatalog(kind: null, search: null, section: null);

		Assert.Multiple(() =>
		{
			Assert.That(response.Items, Is.Empty);
			Assert.That(response.Registry.HasCatalog, Is.False);
		});
	}

	[Test]
	public void Repeated_kind_parameters_narrow_the_catalog_to_exactly_those_kinds()
	{
		SeedBrowseCatalog();

		var response = _controller.GetCatalog(kind: null,
			search: null,
			section: StoreCatalogSection.Name,
			kinds: [StoreExtensionKind.Plugin, StoreExtensionKind.IconPack]);

		Assert.Multiple(() =>
		{
			Assert.That(response.Items.Select(item => item.Id), Is.EqualTo(new[] { "i1", "p1", "p2" }));
			Assert.That(response.Total, Is.EqualTo(3));
			Assert.That(response.Items.Select(item => item.Kind),
				Has.None.EqualTo(StoreExtensionKind.ProfileTemplate));
		});
	}

	[Test]
	public void A_single_kind_parameter_keeps_selecting_only_that_kind()
	{
		SeedBrowseCatalog();

		var plugins = _controller.GetCatalog(kind: StoreExtensionKind.Plugin, search: null, section: null);
		var templates = _controller.GetCatalog(kind: StoreExtensionKind.ProfileTemplate,
			search: null,
			section: null);

		Assert.Multiple(() =>
		{
			Assert.That(plugins.Items.Select(item => item.Id), Is.EquivalentTo(new[] { "p1", "p2" }));
			Assert.That(plugins.Total, Is.EqualTo(2));
			Assert.That(templates.Items.Select(item => item.Id), Is.EquivalentTo(new[] { "t1" }));
			Assert.That(templates.Total, Is.EqualTo(1));
		});
	}

	[Test]
	public void A_page_smaller_than_the_catalog_still_reports_how_much_there_is_to_reach()
	{
		SeedBrowseCatalog();

		var response = _controller.GetCatalog(kind: null,
			search: null,
			section: StoreCatalogSection.Name,
			skip: 0,
			take: 2);

		Assert.Multiple(() =>
		{
			Assert.That(response.Items, Has.Count.EqualTo(2));
			Assert.That(response.Total, Is.EqualTo(4));
		});
	}

	[Test]
	public void A_registry_that_features_nothing_answers_with_an_empty_section_not_an_unavailable_store()
	{
		SeedBrowseCatalog();

		var response = _controller.GetCatalog(kind: null,
			search: null,
			section: StoreCatalogSection.Featured);

		Assert.Multiple(() =>
		{
			Assert.That(response.Items, Is.Empty);
			Assert.That(response.Total, Is.Zero);
		});
	}

	[Test]
	public void Two_install_calls_for_the_same_package_return_the_same_operation_id()
	{
		SeedPlugin();

		var first = _controller.Install(new InstallStoreExtensionRequest
		{
			Kind = StoreExtensionKind.Plugin,
			PackageId = PluginId
		});
		var second = _controller.Install(new InstallStoreExtensionRequest
		{
			Kind = StoreExtensionKind.Plugin,
			PackageId = PluginId
		});

		Assert.That(second.Operation?.Id, Is.EqualTo(first.Operation?.Id));
	}

	[Test]
	public void The_catalog_response_never_carries_an_artifact_url()
	{
		SeedPlugin();

		var response = _controller.GetCatalog(kind: null, search: null, section: null);
		var json = JsonSerializer.Serialize(response);

		Assert.That(json, Does.Not.Contain("cdn.example"));
	}

	[Test]
	public async Task A_screenshot_index_the_entry_does_not_declare_is_refused()
	{
		SeedPlugin();

		var result = await _controller.GetScreenshot(StoreExtensionKind.Plugin,
			PluginId,
			index: 5,
			CancellationToken.None);

		Assert.That(result, Is.InstanceOf<NotFoundResult>());
	}

	// The published registry serves SVG icons, so a media path that only recognises raster magic bytes
	// refuses every real icon. Exercised through the cache so no network is involved.
	[Test]
	public async Task An_svg_icon_is_served_as_an_image_rather_than_refused()
	{
		var svg = global::System.Text.Encoding.UTF8.GetBytes(
			"<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"></svg>");
		SeedPluginWithIcon(svg);

		var result = await _controller.GetIcon(StoreExtensionKind.Plugin, PluginId, CancellationToken.None);

		Assert.That(result, Is.TypeOf<FileContentResult>());
		Assert.That(((FileContentResult)result).ContentType, Is.EqualTo("image/svg+xml"));
	}

	[Test]
	public async Task A_screenshot_index_the_verified_entry_does_not_declare_is_refused()
	{
		SeedPlugin();

		var result = await _controller.GetScreenshot(StoreExtensionKind.Plugin,
			PluginId,
			3,
			CancellationToken.None);

		Assert.That(result, Is.TypeOf<NotFoundResult>());
	}

	[Test]
	public void The_detail_body_reports_the_download_size_and_the_operating_systems_a_package_supports()
	{
		SeedPlugin(supportedRids: _appleAndWindowsRids, size: 435318);

		var response = _controller.GetExtension(StoreExtensionKind.Plugin, PluginId);

		Assert.Multiple(() =>
		{
			Assert.That(response.Extension!.DownloadSize, Is.EqualTo(435318));
			Assert.That(response.Extension!.SupportedOperatingSystems, Is.EqualTo(_expectedOperatingSystems));
		});
	}

	[Test]
	public void A_package_that_declares_no_runtime_identifiers_reports_no_operating_system_restriction()
	{
		SeedPlugin();

		var response = _controller.GetExtension(StoreExtensionKind.Plugin, PluginId);

		Assert.That(response.Extension!.SupportedOperatingSystems, Is.Empty);
	}

	[Test]
	public async Task A_failed_uninstall_surfaces_the_service_error_as_the_response_transport_error_code()
	{
		_uninstallService.Next = Result.Fail<StoreUninstallError>(StoreUninstallError.DependencyInUse, "in use");

		var response = await _controller.Uninstall(
			new UninstallStoreExtensionRequest { Kind = StoreExtensionKind.Plugin, Id = PluginId },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(StoreUninstallError.DependencyInUse)));
		});
	}

	private void SeedPluginWithIcon(byte[] iconBytes)
	{
		var digest = Convert.ToHexStringLower(global::System.Security.Cryptography.SHA256.HashData(iconBytes));
		File.WriteAllBytes(Path.Combine(_paths.StoreMediaDirectory, digest), iconBytes);
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
						Size = 16,
						Icon = new StoreMediaAsset
						{
							Url = new Uri("https://cdn.example/icon.svg"),
							Sha256 = digest,
							Size = iconBytes.LongLength,
							ContentType = "image/svg+xml"
						}
					}
				}
			]
		});
	}

	private void SeedBrowseCatalog() =>
		_catalog.Swap(new StoreCatalogSnapshot
		{
			Sequence = 1,
			Entries =
			[
				BrowseEntry("p1", "Plugin One", StoreExtensionKind.Plugin),
				BrowseEntry("p2", "Plugin Two", StoreExtensionKind.Plugin),
				BrowseEntry("i1", "Icons", StoreExtensionKind.IconPack),
				BrowseEntry("t1", "Template", StoreExtensionKind.ProfileTemplate)
			]
		});

	private static StoreCatalogEntry BrowseEntry(string id, string name, StoreExtensionKind kind) =>
		new()
		{
			Kind = kind,
			Id = id,
			Name = name,
			LatestVersion = "1.0.0",
			LatestRelease = new StoreReleaseManifest
			{
				Version = "1.0.0",
				ArtifactUrl = new Uri($"https://cdn.example/{id}.bin"),
				Sha256 = new string('a', 64),
				Size = 16
			}
		};

	private void SeedPlugin(string[]? supportedRids = null, long size = 16)
	{
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
					SupportedRids = supportedRids ?? [],
					LatestRelease = new StoreReleaseManifest
					{
						Version = "1.0.0",
						ArtifactUrl = new Uri("https://cdn.example/hue.macroDeckPlugin"),
						Sha256 = new string('a', 64),
						Size = size
					}
				}
			]
		});
	}
}

internal sealed class FakeStoreRegistryRefresher : IStoreRegistryRefresher
{
	public StoreRegistryStatus Status => StoreRegistryStatus.Unavailable;

	public Task<Result<RegistryRefreshError>> Refresh(CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task LoadCachedRegistry(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeStoreUpdateDetector : IStoreUpdateDetector
{
	public int CheckCallCount { get; private set; }

	public IReadOnlyList<StoreAvailableUpdate> Check()
	{
		CheckCallCount++;
		return [];
	}
}

internal sealed class ThrowingStoreArtifactDownloader : IStoreArtifactDownloader
{
	public Task<StoreArtifactDownloadResult> Download(Uri artifactUrl,
		string expectedSha256Hex,
		long expectedSize,
		Guid operationId,
		IProgress<StoreArtifactDownloadProgress>? progress,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}

internal sealed class FakeStoreUninstallService : IStoreUninstallService
{
	public Result<StoreUninstallError> Next { get; set; } = Result.Ok<StoreUninstallError>();

	public Task<Result<StoreUninstallError>> Uninstall(StoreExtensionKind kind,
		string packageId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult(Next);
}
