using System.Text.Json;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Infrastructure.Migration;
using MacroDeckHost.Tests.UnitTests.Portable;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Migration;

/// <summary>
/// Wires a real <see cref="MigrationService" /> onto the same fakes the portability tests use, so a plan
/// can be applied end to end and the effect observed: profiles in the cache, icons in the catalogue,
/// secrets in the store and configuration entries against the integrations.
/// </summary>
internal sealed class MigrationTestHarness : IDisposable
{
	public MigrationTestHarness(params IMigrationSource[] sources)
	{
		Portability = new PortabilityTestHarness();
		IconPacks = new IconPackService(Portability.Icons.Cache,
			Portability.Icons.BatchTracker,
			Portability.Icons.Storage,
			Portability.Icons.Mediator,
			new IconPackOwnerRegistry([]),
			Portability.Icons.Logger);
		IconImport = Portability.Icons.CreateImportService();
		ConfigStore = new RecordingConfigStore();
		Lifecycle = new FakeIntegrationLifecycle();
		KeyRing = new UnlockedKeyRing();

		Service = new MigrationService(sources,
			Portability.ProfileService,
			IconPacks,
			IconImport,
			Portability.Secrets,
			ConfigStore,
			Portability.Integrations,
			Lifecycle,
			Portability.Variables,
			KeyRing,
			Portability.Icons.Logger);
	}

	public PortabilityTestHarness Portability { get; }

	public IconPackService IconPacks { get; }

	public IconImportService IconImport { get; }

	public RecordingConfigStore ConfigStore { get; }

	public FakeIntegrationLifecycle Lifecycle { get; }

	public UnlockedKeyRing KeyRing { get; }

	public MigrationService Service { get; }

	public void Dispose() => Portability.Dispose();

	internal sealed class RecordingConfigStore : IIntegrationConfigStore
	{
		private readonly List<ConfigEntryRecord> _entries = [];

		public IReadOnlyList<ConfigEntryRecord> Entries => _entries;

		public Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
			=> Task.FromResult<IReadOnlyList<ConfigEntrySummary>>(_entries
				.Where(entry => entry.IntegrationId == integrationId)
				.Select(entry => new ConfigEntrySummary(entry.Id, entry.IntegrationId, entry.Title, entry.CreatedAt))
				.ToList());

		public Task<ConfigEntryRecord?> Find(Guid entryId)
			=> Task.FromResult(_entries.FirstOrDefault(entry => entry.Id == entryId));

		public Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values)
		{
			var id = Guid.NewGuid();
			_entries.Add(new ConfigEntryRecord(id, integrationId, title, DateTime.UtcNow, values));
			return Task.FromResult(id);
		}

		public Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
			=> Task.FromResult(false);

		public Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
			=> Task.FromResult(false);

		public Task Delete(Guid entryId)
		{
			_entries.RemoveAll(entry => entry.Id == entryId);
			return Task.CompletedTask;
		}
	}

	internal sealed class UnlockedKeyRing : IKeyRingProtectionService
	{
		public KeyRingProtectionState State { get; set; } = KeyRingProtectionState.Unprotected;

		public KeyRingProtectionStatus Status
			=> new(State,
				KeyRingLockReason.None,
				KeyRingUnprotectedReason.None,
				KeyRingBackend.None,
				BackendAvailable: false,
				BackendUnavailableReason: null,
				KekId: null,
				EscrowWrapCount: 0,
				RecoveryKeyExported: false,
				MigrationPending: false);

		public Task<Result<KeyRingProtectionError>> EnsureProtected(ReadOnlyMemory<byte> recoveryKey,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(Result.Ok<KeyRingProtectionError>());

		public Task<Result<KeyRingProtectionError>> AddEscrowWrap(ReadOnlyMemory<byte> recoveryKey,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(Result.Ok<KeyRingProtectionError>());

		public Task<Result<KeyRingProtectionError>> PruneEscrowWrapsExcept(ReadOnlyMemory<byte> recoveryKey,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(Result.Ok<KeyRingProtectionError>());

		public Task<Result<KeyRingProtectionError>> Unlock(ReadOnlyMemory<byte> recoveryKey,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(Result.Ok<KeyRingProtectionError>());

		public Task<Result<KeyRingProtectionError>> RewrapPending(CancellationToken cancellationToken = default)
			=> Task.FromResult(Result.Ok<KeyRingProtectionError>());

		public Result<KeyRingProtectionError> AdoptKek(ReadOnlyMemory<byte> kek)
			=> Result.Ok<KeyRingProtectionError>();
	}
}
