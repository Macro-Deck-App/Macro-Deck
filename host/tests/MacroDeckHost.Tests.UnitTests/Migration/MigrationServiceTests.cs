using System.Text.Json;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Persistence.Profiles;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Migration.MacroDeck2;

namespace MacroDeckHost.Tests.UnitTests.Migration;

[TestFixture]
public class MigrationServiceTests
{
	private const string SourceId = "stub";

	private const string IntegrationId = "app.macro-deck.obs";

	private MigrationTestHarness _harness = null!;
	private StubMigrationSource _source = null!;

	[SetUp]
	public void SetUp()
	{
		_source = new StubMigrationSource();
		_harness = new MigrationTestHarness(_source);
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	// The whole point of the preview is that the user decides before anything is written, so this asserts
	// the absence of every side effect the apply path has.
	[Test]
	public async Task Preview_WritesNothing()
	{
		_source.Plan = PlanWith(Profile("Deck"),
			variables: [new MigratedVariable("counter", VariableType.Numeric, "1", null)]);
		_source.Plan.IntegrationConfigs.Add(Config());

		var result = await _harness.Service.Preview(Request(), CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Portability.ProfileCache.GetAll(), Is.Empty);
			Assert.That(_harness.ConfigStore.Entries, Is.Empty);
			Assert.That(_harness.Portability.Secrets.Count, Is.Zero);
			Assert.That(await _harness.Portability.Variables.GetAll(), Is.Empty);
			Assert.That(_harness.Lifecycle.ReinitializeCalls, Is.Empty);
		});
	}

	[Test]
	public async Task Apply_CreatesTheProfileThroughTheExistingImporter()
	{
		_source.Plan = PlanWith(Profile("Deck"));

		var result = await _harness.Service.Apply(Request(), CancellationToken.None);

		var created = _harness.Portability.ProfileCache.GetAll().Single();
		var folders = _harness.Portability.FolderCache.GetFoldersByProfileId(created.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.CreatedProfileIds, Is.EquivalentTo(new[] { created.Id }));
			Assert.That(created.Name, Is.EqualTo("Deck"));
			Assert.That(folders.Single().IsDefault, Is.True, "the imported root becomes the profile's start folder");
			Assert.That(folders.Single().Widgets, Has.Count.EqualTo(1));
		});
	}

	// A stored configuration entry on its own leaves the integration switched off, which is not what
	// "migrated" means to anyone looking at the result.
	[Test]
	public async Task Apply_EnablesAndReinitializesEachIntegrationItConfigures()
	{
		_harness.Portability.Integrations.SetEnabled(IntegrationId, false);
		_source.Plan = PlanWith(Profile("Deck"));
		_source.Plan.IntegrationConfigs.Add(Config());

		await _harness.Service.Apply(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.ConfigStore.Entries.Single().IntegrationId, Is.EqualTo(IntegrationId));
			Assert.That(_harness.Portability.Integrations.IsEnabled(IntegrationId), Is.True);
			Assert.That(_harness.Lifecycle.ReinitializeCalls, Is.EquivalentTo(new[] { IntegrationId }));
		});
	}

	[Test]
	public async Task Apply_StoresCredentialsAsSecretsAndReferencesThemFromTheEntry()
	{
		_source.Plan = PlanWith(Profile("Deck"));
		_source.Plan.IntegrationConfigs.Add(Config(secret: "hunter2"));

		await _harness.Service.Apply(Request(), CancellationToken.None);

		var entry = _harness.ConfigStore.Entries.Single();
		var reference = entry.Values["password"];

		Assert.Multiple(async () =>
		{
			Assert.That(SecretReferenceJson.TryGet(reference, out var secretId),
				Is.True,
				"the entry references a secret rather than holding the value");
			Assert.That(await _harness.Portability.Secrets.Resolve(secretId), Is.EqualTo("hunter2"));
			Assert.That(entry.Values["host"].GetString(), Is.EqualTo("127.0.0.1"));
			Assert.That(reference.GetRawText(), Does.Not.Contain("hunter2"));
		});
	}

	// Migrating twice is a thing people do. Profiles are additive, as importing an archive twice is, but an
	// integration must not end up with a second account entry it never asked for.
	[Test]
	public async Task Apply_RunTwice_DoesNotConfigureTheSameIntegrationAgain()
	{
		_source.Plan = PlanWith(Profile("Deck"));
		_source.Plan.IntegrationConfigs.Add(Config());

		await _harness.Service.Apply(Request(), CancellationToken.None);
		_source.Plan = PlanWith(Profile("Deck"));
		_source.Plan.IntegrationConfigs.Add(Config());
		var second = await _harness.Service.Apply(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.ConfigStore.Entries, Has.Count.EqualTo(1));
			Assert.That(second.Data!.Plan.Warnings.Select(warning => warning.Kind),
				Does.Contain(MigrationWarningKind.AlreadyConfigured));
			Assert.That(_harness.Portability.ProfileCache.GetAll(),
				Has.Count.EqualTo(2),
				"profiles are additive, exactly as importing the same archive twice is");
		});
	}

	[Test]
	public async Task Apply_MigratesGlobalVariablesAndReportsOnesItCannotCreate()
	{
		_source.Plan = PlanWith(Profile("Deck"),
			variables:
			[
				new MigratedVariable("counter", VariableType.Numeric, "7", null),
				new MigratedVariable("counter", VariableType.Text, "again", null)
			]);

		var result = await _harness.Service.Apply(Request(), CancellationToken.None);
		var stored = await _harness.Portability.Variables.GetAll();

		Assert.Multiple(() =>
		{
			Assert.That(stored.Select(variable => variable.Name), Is.EquivalentTo(new[] { "counter" }));
			Assert.That(stored.Single().Scope, Is.EqualTo(VariableScope.Global));
			Assert.That(result.Data!.Plan.Warnings.Select(warning => warning.Kind),
				Does.Contain(MigrationWarningKind.SkippedVariable));
		});
	}

	// Secrets go through Data Protection, which cannot generate keys while the ring is locked. Failing up
	// front says so, instead of writing half a migration and then throwing.
	[Test]
	public async Task Apply_WhileTheKeyRingIsLocked_RefusesBeforeWritingAnything()
	{
		_harness.KeyRing.State = KeyRingProtectionState.Locked;
		_source.Plan = PlanWith(Profile("Deck"));

		var result = await _harness.Service.Apply(Request(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(MigrationError.KeyRingLocked));
			Assert.That(_harness.Portability.ProfileCache.GetAll(), Is.Empty);
		});
	}

	// Applying from a backup archive reads files the source unpacked, long after it finished reading. The
	// unpacked copy therefore has to outlive the plan's own read - it did not, and every migration from an
	// archive failed on its first icon while previewing the same archive worked.
	[Test]
	public async Task Apply_FromABackupArchive_ImportsTheIconsThatArchiveHeld()
	{
		using var fixture = new MacroDeck2Fixture();
		fixture.WithConfig()
			.WithIconPack("Powered Steel", "play")
			.WithProfile("0",
				"Backed up",
				rows: 3,
				columns: 5,
				folders:
				[
					MacroDeck2Fixture.Folder("root",
						"*Root*",
						[MacroDeck2Fixture.Button(0, 0, iconOff: "Powered Steel.play", iconOn: "Powered Steel.play")])
				])
			.Build();

		var backup = fixture.PackAsBackup();
		using var harness = new MigrationTestHarness(new MacroDeck2MigrationSource(new NoMigrators(),
			new Serilog.LoggerConfiguration().CreateLogger()));

		try
		{
			var result = await harness.Service.Apply(
				new MigrationRequest(MacroDeck2MigrationSource.SourceId, backup, null, false),
				CancellationToken.None);

			Assert.That(result.Success, Is.True, result.ErrorMessage);
			var imported = harness.Portability.Icons.Cache.GetAllPacks()
				.Sum(pack => harness.Portability.Icons.Cache.GetIconsByPackId(pack.Id).Count);

			Assert.Multiple(() =>
			{
				Assert.That(harness.Portability.ProfileCache.GetAll(), Has.Count.EqualTo(1));
				Assert.That(imported, Is.EqualTo(1), "the icon the archive held is in the catalogue");
			});
		}
		finally
		{
			File.Delete(backup);
		}
	}

	// The unpacked copy is transient: nothing may be left behind once the migration is over.
	[Test]
	public async Task Apply_FromABackupArchive_RemovesWhatItUnpacked()
	{
		using var fixture = new MacroDeck2Fixture();
		fixture.WithConfig()
			.WithProfile("0", "Backed up", 3, 5, [MacroDeck2Fixture.Folder("root", "*Root*", [])])
			.Build();

		var backup = fixture.PackAsBackup();
		using var harness = new MigrationTestHarness(new MacroDeck2MigrationSource(new NoMigrators(),
			new Serilog.LoggerConfiguration().CreateLogger()));

		var before = Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "macro-deck-migration"),
			"*",
			SearchOption.TopDirectoryOnly).Length;

		try
		{
			await harness.Service.Apply(new MigrationRequest(MacroDeck2MigrationSource.SourceId, backup, null, false),
				CancellationToken.None);

			var after = Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "macro-deck-migration"),
				"*",
				SearchOption.TopDirectoryOnly).Length;

			Assert.That(after, Is.EqualTo(before));
		}
		finally
		{
			File.Delete(backup);
		}
	}

	[Test]
	public async Task Preview_WithASourceThatIsNotRegistered_Fails()
	{
		var result = await _harness.Service.Preview(new MigrationRequest("nothing-like-this", "/tmp", null, false),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(MigrationError.UnknownSource));
		});
	}

	[Test]
	public void GetSources_ReportsEverySourceAndWhereItFoundIt()
		=> Assert.That(_harness.Service.GetSources().Single(),
			Is.EqualTo(new MigrationSourceDescriptor(SourceId, "Stub", "/detected")));

	private static MigrationRequest Request() => new(SourceId, "/tmp/source", null, false);

	private static MigrationPlan PlanWith(PortableContent profile, IReadOnlyList<MigratedVariable>? variables = null)
	{
		var plan = new MigrationPlan { SourceId = SourceId, SourceName = "Stub" };
		plan.Profiles.Add(profile);
		plan.Variables.AddRange(variables ?? []);
		return plan;
	}

	private static MigratedConfiguration Config(string? secret = null)
		=> new(IntegrationId,
			"OBS",
			new Dictionary<string, JsonElement> { ["host"] = JsonSerializer.SerializeToElement("127.0.0.1") },
			secret is null
				? new Dictionary<string, MigratedSecret>()
				: new Dictionary<string, MigratedSecret> { ["password"] = new(secret, MigratedSecretKind.Password) });

	private static PortableContent Profile(string name)
		=> new()
		{
			Kind = PortableArchiveKind.Profile,
			Profile = new ProfileFile
			{
				Id = Guid.NewGuid(),
				Name = name,
				DefaultRows = 3,
				DefaultColumns = 5,
				Folders =
				[
					new ProfileFolder
					{
						Id = Guid.NewGuid(),
						Name = "Root",
						CreatedAt = DateTime.UtcNow,
						Widgets =
						[
							new ProfileWidget
							{
								Id = Guid.NewGuid(),
								Type = WidgetTypeIds.ActionButton,
								Data = """{"label":"Hi"}"""
							}
						]
					}
				]
			}
		};

	private sealed class NoMigrators : IMigrationActionRegistry
	{
		public IIntegrationMigration? FindByActionSource(MigrationSource source, string actionSource) => null;

		public IIntegrationMigration? FindBySettingsSource(MigrationSource source, string settingsSource) => null;

		public IReadOnlyList<MigrationSource> SupportedSources() => [];
	}

	private sealed class StubMigrationSource : IMigrationSource
	{
		public MigrationPlan Plan { get; set; } = new();

		public string Id => SourceId;

		public string Name => "Stub";

		public string? TryDetectDefaultPath() => "/detected";

		public bool Recognizes(string path) => true;

		public Task<Result<MigrationPlan, MigrationError>> Read(
			MigrationRequest request,
			CancellationToken cancellationToken)
			=> Task.FromResult(Result.Ok<MigrationPlan, MigrationError>(Plan));
	}
}
