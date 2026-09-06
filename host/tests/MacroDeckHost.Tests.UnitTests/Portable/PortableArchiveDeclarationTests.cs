using System.Text.Json;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Portable;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Portable;

[TestFixture]
public class PortableArchiveDeclarationTests
{
	private const string ObsId = "app.macro-deck.obs";
	private const string KeyboardId = "app.macro-deck.keyboard";
	private const string MusicPlayerId = "app.macro-deck.music-player";

	private PortabilityTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new PortabilityTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Export_WithoutIcons_LeavesTheIconsOutOfTheArchive()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var (source, _, _) = await _harness.SeedProfile($"{{\"iconId\":\"{icon.Id}\"}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default with { IncludeIcons = false },
			CancellationToken.None);

		var read = PortableArchive.Read(export.Data!, password: null);
		Assert.Multiple(() =>
		{
			Assert.That(read.Content!.Icons, Is.Empty);
			Assert.That(read.Icons, Is.Empty);
			Assert.That(read.Manifest!.Contents.IconCount, Is.Zero);
		});
	}

	[Test]
	public async Task Export_WithIcons_IsStillTheDefault()
	{
		var pack = await _harness.Icons.CreatePack("Source Pack");
		var icon = await _harness.AddReadyIcon(pack.Id, "star", sizes: [128]);
		var (source, _, _) = await _harness.SeedProfile($"{{\"iconId\":\"{icon.Id}\"}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var manifest = PortableArchive.ReadManifest(export.Data!)!;
		Assert.That(manifest.Contents.IconCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Export_DeclaresTheIntegrationsTheWidgetsUse()
	{
		var (source, _, _) = await _harness.SeedProfile($"{{\"integrationId\":\"{ObsId}\"}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var declared = PortableArchive.ReadManifest(export.Data!)!.Contents.Integrations;
		Assert.Multiple(() =>
		{
			Assert.That(declared.Select(i => i.Id), Is.EqualTo(new[] { ObsId }));
			Assert.That(declared[0].Name, Is.EqualTo("OBS Studio"));
			Assert.That(declared[0].RequiresConfiguration, Is.True);
		});
	}

	[Test]
	public async Task Export_DeclaresTheIntegrationOfAnEventTriggerToo()
	{
		var (source, _, _) = await _harness.SeedProfile($"{{\"event\":{{\"providerId\":\"{KeyboardId}\"}}}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var declared = PortableArchive.ReadManifest(export.Data!)!.Contents.Integrations;
		Assert.Multiple(() =>
		{
			Assert.That(declared.Select(i => i.Id), Is.EqualTo(new[] { KeyboardId }));
			Assert.That(declared[0].RequiresConfiguration, Is.False);
		});
	}

	[Test]
	public async Task Export_DeclaresAnIntegrationOnlyABundledScriptUses()
	{
		var script = (await _harness.Scripts.Create("Start stream",
			"Opens OBS",
			$"[{{\"integrationId\":\"{ObsId}\"}}]")).Data!;
		var (source, _, _) = await _harness.SeedProfile($"{{\"scriptId\":\"{script.Id}\"}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var manifest = PortableArchive.ReadManifest(export.Data!)!;
		Assert.Multiple(() =>
		{
			Assert.That(manifest.Contents.ScriptCount, Is.EqualTo(1));
			Assert.That(manifest.Contents.Integrations.Select(i => i.Id), Is.EqualTo(new[] { ObsId }));
		});
	}

	[Test]
	public async Task Export_DoesNotDeclareSystemIntegrations()
	{
		var (source, _, _) = await _harness.SeedProfile($"{{\"integrationId\":\"{MusicPlayerId}\"}}");

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		Assert.That(PortableArchive.ReadManifest(export.Data!)!.Contents.Integrations, Is.Empty);
	}

	[Test]
	public async Task Export_SummarisesWhatTheArchiveHolds()
	{
		var profile = await _harness.AddProfile("Streaming");
		var root = await _harness.AddFolder(profile.Id, "Root");
		var child = await _harness.AddFolder(profile.Id, "Scenes", root.Id);
		_harness.AddWidget(root.Id, "{\"label\":\"a\"}");
		_harness.AddWidget(child.Id, "{\"label\":\"b\"}");

		var export = await _harness.ProfileService.Export(profile.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var contents = PortableArchive.ReadManifest(export.Data!)!.Contents;
		Assert.Multiple(() =>
		{
			Assert.That(contents.Name, Is.EqualTo("Streaming"));
			Assert.That(contents.FolderCount, Is.EqualTo(2));
			Assert.That(contents.WidgetCount, Is.EqualTo(2));
			Assert.That(contents.SecretCount, Is.Zero);
			Assert.That(contents.VariableCount, Is.Zero);
		});
	}

	[Test]
	public async Task Export_SummarisesTheWidgetScopedVariableItCarries()
	{
		var (source, _, widget) = await _harness.SeedProfile(null);
		await _harness.AddWidgetVariable(widget.Id, "mine", VariableClassification.User);

		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		Assert.That(PortableArchive.ReadManifest(export.Data!)!.Contents.VariableCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Export_OfAFolderWithoutItsSubfolders_CountsOnlyWhatTravels()
	{
		var profile = await _harness.AddProfile("Streaming");
		var source = await _harness.AddFolder(profile.Id, "Lights");
		var child = await _harness.AddFolder(profile.Id, "Scenes", source.Id);
		_harness.AddWidget(source.Id, "{\"label\":\"a\"}");
		_harness.AddWidget(child.Id, "{\"label\":\"b\"}");

		var export = await _harness.FolderService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);

		var contents = PortableArchive.ReadManifest(export.Data!)!.Contents;
		Assert.Multiple(() =>
		{
			Assert.That(contents.Name, Is.EqualTo("Lights"));
			Assert.That(contents.FolderCount, Is.EqualTo(1));
			Assert.That(contents.WidgetCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Inspect_AnEncryptedArchive_AnswersWithoutThePassword()
	{
		var secretId = _harness.Secrets.Store("hunter2", SecretKind.Password);
		var (source, _, _) = await _harness.SeedProfile(
			$"{{\"integrationId\":\"{ObsId}\",\"$secret\":\"{secretId}\"}}");
		var export = await _harness.ProfileService.Export(source.Id,
			new PortableExportOptions { IncludeSecrets = true, Password = "export-password" },
			CancellationToken.None);

		var info = (await Inspector().Inspect(export.Data!, CancellationToken.None)).Data!;

		Assert.Multiple(() =>
		{
			Assert.That(info.Kind, Is.EqualTo(PortableArchiveKind.Profile));
			Assert.That(info.Name, Is.EqualTo("Source"));
			Assert.That(info.Encrypted, Is.True);
			Assert.That(info.IncludesSecrets, Is.True);
			Assert.That(info.SecretCount, Is.EqualTo(1));
			Assert.That(info.Integrations.Select(i => i.Id), Is.EqualTo(new[] { ObsId }));
		});
	}

	[Test]
	public async Task Inspect_ReportsAConfigurableIntegrationAsNotConfigured_UntilItHasAnEntry()
	{
		var archive = await ArchiveRequiring(ObsId);

		var before = (await Inspector().Inspect(archive, CancellationToken.None)).Data!;

		var store = new StubConfigStore();
		store.Add(ObsId);
		var after = (await Inspector(store).Inspect(archive, CancellationToken.None)).Data!;

		Assert.Multiple(() =>
		{
			Assert.That(before.Integrations[0].Availability,
				Is.EqualTo(PortableIntegrationAvailability.NotConfigured));
			Assert.That(after.Integrations[0].Availability, Is.EqualTo(PortableIntegrationAvailability.Ready));
		});
	}

	[Test]
	public async Task Inspect_ReportsAnIntegrationWithoutAConfigFlowAsReady()
	{
		var archive = await ArchiveRequiring(KeyboardId);

		var info = (await Inspector().Inspect(archive, CancellationToken.None)).Data!;

		Assert.That(info.Integrations[0].Availability, Is.EqualTo(PortableIntegrationAvailability.Ready));
	}

	[Test]
	public async Task Inspect_ReportsADisabledIntegrationAsDisabled()
	{
		var archive = await ArchiveRequiring(KeyboardId);
		_harness.Integrations.SetEnabled(KeyboardId, false);

		var info = (await Inspector().Inspect(archive, CancellationToken.None)).Data!;

		Assert.That(info.Integrations[0].Availability, Is.EqualTo(PortableIntegrationAvailability.Disabled));
	}

	[Test]
	public async Task Inspect_ReportsAnIntegrationThisMachineDoesNotHaveAsMissing_KeepingItsExportedName()
	{
		var archive = await ArchiveRequiring(ObsId);
		var withoutObs = new ConfigurableIntegrationRegistry([StubIntegration.Create(KeyboardId, "Keyboard")]);

		var inspector = new PortableArchiveInspector(withoutObs, new StubConfigStore());
		var info = (await inspector.Inspect(archive, CancellationToken.None)).Data!;

		Assert.Multiple(() =>
		{
			Assert.That(info.Integrations[0].Availability, Is.EqualTo(PortableIntegrationAvailability.Missing));
			Assert.That(TestLocalization.Resolve(info.Integrations[0].Name), Is.EqualTo("OBS Studio"));
		});
	}

	[Test]
	public async Task Inspect_AGarbageFile_ReturnsInvalidArchive()
	{
		var result = await Inspector().Inspect([1, 2, 3, 4], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.InvalidArchive));
		});
	}

	private async Task<byte[]> ArchiveRequiring(string integrationId)
	{
		var (source, _, _) = await _harness.SeedProfile($"{{\"integrationId\":\"{integrationId}\"}}");
		var export = await _harness.ProfileService.Export(source.Id,
			PortableExportOptions.Default,
			CancellationToken.None);
		return export.Data!;
	}

	private PortableArchiveInspector Inspector(StubConfigStore? store = null)
		=> new(_harness.Integrations, store ?? new StubConfigStore());

	private sealed class StubConfigStore : IIntegrationConfigStore
	{
		private readonly List<ConfigEntrySummary> _entries = [];

		public void Add(string integrationId)
			=> _entries.Add(new ConfigEntrySummary(Guid.NewGuid(), integrationId, "Entry", DateTime.UtcNow));

		public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySummary>>(_entries
				.Where(entry => entry.IntegrationId == integrationId).ToList());

		public Task<ConfigEntryRecord?> Find(Guid entryId) => Task.FromResult<ConfigEntryRecord?>(null);

		public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
			=> throw new NotSupportedException();

		public Task Delete(Guid entryId) => throw new NotSupportedException();
	}
}
