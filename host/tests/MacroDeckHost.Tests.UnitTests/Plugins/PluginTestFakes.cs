using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

internal sealed class InMemoryPluginAccessTokenRepository : IPluginAccessTokenRepository
{
	public List<PluginAccessTokenEntity> Tokens { get; } = [];

	public Task<PluginAccessTokenEntity?> GetById(Guid id) => Task.FromResult(Tokens.FirstOrDefault(t => t.Id == id));

	public Task<PluginAccessTokenEntity?> GetByHash(string tokenHash)
		=> Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

	public Task<IReadOnlyList<PluginAccessTokenEntity>> GetAll()
		=> Task.FromResult<IReadOnlyList<PluginAccessTokenEntity>>(Tokens.OrderByDescending(t => t.CreatedAt)
			.ToList());

	public Task Create(PluginAccessTokenEntity token)
	{
		Tokens.Add(token);
		return Task.CompletedTask;
	}

	public Task TouchLastUsed(Guid id, DateTime at)
	{
		var token = Tokens.FirstOrDefault(t => t.Id == id);
		if (token is not null)
		{
			token.LastUsedAt = at;
		}

		return Task.CompletedTask;
	}

	public Task Revoke(Guid id, DateTime at)
	{
		var token = Tokens.FirstOrDefault(t => t.Id == id && t.RevokedAt is null);
		if (token is not null)
		{
			token.RevokedAt = at;
		}

		return Task.CompletedTask;
	}

	public Task Delete(Guid id)
	{
		Tokens.RemoveAll(t => t.Id == id);
		return Task.CompletedTask;
	}
}

internal sealed class InMemoryPluginRegistrationRepository : IPluginRegistrationRepository
{
	public List<PluginRegistrationEntity> Registrations { get; } = [];

	public Task<PluginRegistrationEntity?> GetByPluginId(string pluginId)
		=> Task.FromResult(Registrations.FirstOrDefault(r => r.PluginId == pluginId));

	public Task<IReadOnlyList<PluginRegistrationEntity>> GetByAccessTokenId(Guid accessTokenId)
		=> Task.FromResult<IReadOnlyList<PluginRegistrationEntity>>(Registrations
			.Where(r => r.AccessTokenId == accessTokenId).ToList());

	public Task<IReadOnlyList<PluginRegistrationEntity>> GetAll()
		=> Task.FromResult<IReadOnlyList<PluginRegistrationEntity>>(Registrations
			.OrderByDescending(r => r.CreatedAt).ToList());

	public Task<IReadOnlyList<PluginRegistrationEntity>> GetNonRevoked()
		=> Task.FromResult<IReadOnlyList<PluginRegistrationEntity>>(Registrations
			.Where(r => r.RevokedAt is null).ToList());

	public Task Create(PluginRegistrationEntity registration)
	{
		if (Registrations.Any(r => r.PluginId == registration.PluginId))
		{
			throw new PluginRegistrationConflictException(registration.PluginId);
		}

		Registrations.Add(registration);
		return Task.CompletedTask;
	}

	public Task<bool> Reactivate(string pluginId,
		string displayName,
		string secretHash,
		Guid? accessTokenId,
		string origin,
		DateTime at)
	{
		var registration = Registrations.FirstOrDefault(r => r.PluginId == pluginId && r.RevokedAt is not null);
		if (registration is null)
		{
			return Task.FromResult(false);
		}

		registration.DisplayName = displayName;
		registration.SecretHash = secretHash;
		registration.AccessTokenId = accessTokenId;
		registration.Origin = origin;
		registration.RevokedAt = null;
		registration.LastSeenAt = null;
		registration.CreatedAt = at;

		return Task.FromResult(true);
	}

	public Task<bool> RotateSecret(string pluginId,
		string displayName,
		string secretHash,
		Guid? accessTokenId,
		string origin)
	{
		var registration = Registrations.FirstOrDefault(r => r.PluginId == pluginId && r.RevokedAt is null);
		if (registration is null)
		{
			return Task.FromResult(false);
		}

		registration.DisplayName = displayName;
		registration.SecretHash = secretHash;
		registration.AccessTokenId = accessTokenId;
		registration.Origin = origin;
		registration.LastSeenAt = null;

		return Task.FromResult(true);
	}

	public Task TouchLastSeen(string pluginId, DateTime at)
	{
		var registration = Registrations.FirstOrDefault(r => r.PluginId == pluginId);
		if (registration is not null)
		{
			registration.LastSeenAt = at;
		}

		return Task.CompletedTask;
	}

	public Task Revoke(string pluginId, DateTime at)
	{
		var registration = Registrations.FirstOrDefault(r => r.PluginId == pluginId && r.RevokedAt is null);
		if (registration is not null)
		{
			registration.RevokedAt = at;
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<string>> RevokeByAccessTokenId(Guid accessTokenId, DateTime at)
	{
		var affected = Registrations.Where(r => r.AccessTokenId == accessTokenId && r.RevokedAt is null).ToList();
		foreach (var registration in affected)
		{
			registration.RevokedAt = at;
		}

		return Task.FromResult<IReadOnlyList<string>>(affected.Select(r => r.PluginId).ToList());
	}

	public Task<IReadOnlyList<string>> DeleteByAccessTokenId(Guid accessTokenId)
	{
		var affected = Registrations.Where(r => r.AccessTokenId == accessTokenId).Select(r => r.PluginId).ToList();
		Registrations.RemoveAll(r => r.AccessTokenId == accessTokenId);

		return Task.FromResult<IReadOnlyList<string>>(affected);
	}
}

internal sealed class FakePluginSessionTokenIssuer : IPluginSessionTokenIssuer
{
	public List<(string PluginId, string SessionId)> Issued { get; } = [];

	public string Issue(string pluginId, string sessionId)
	{
		Issued.Add((pluginId, sessionId));
		return $"session-token:{pluginId}:{sessionId}";
	}
}

internal sealed class FakePluginConnection : IPluginConnection
{
	public string ConnectionId { get; }

	public List<ProtocolEnvelope> Sent { get; } = [];

	public List<(int CloseCode, string Reason)> Closes { get; } = [];

	public TaskCompletionSource<bool>? SendGate { get; set; }

	public FakePluginConnection(string connectionId = "fake-connection")
	{
		ConnectionId = connectionId;
	}

	public async Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
	{
		Sent.Add(envelope);

		if (SendGate is { } gate)
		{
			await gate.Task.WaitAsync(cancellationToken);
		}
	}

	public Task Close(int closeCode, string reason, CancellationToken cancellationToken = default)
	{
		Closes.Add((closeCode, reason));
		return Task.CompletedTask;
	}
}

internal sealed class FakePluginManifestReader : IPluginManifestReader
{
	public PluginManifestReadResult? Default { get; set; }

	public Dictionary<string, PluginManifestReadResult> ResultsByPath { get; } = new(StringComparer.Ordinal);

	public List<(string ManifestPath, string ExpectedPluginId, string ExpectedVersion)> Reads { get; } = [];

	public PluginManifestReadResult Read(string manifestPath, string expectedPluginId, string expectedVersion)
	{
		Reads.Add((manifestPath, expectedPluginId, expectedVersion));

		if (ResultsByPath.TryGetValue(manifestPath, out var byPath))
		{
			return byPath;
		}

		return Default ?? PluginManifestReadResult.Fail(PluginManifestError.NotFound, "No fake result configured.");
	}

	public PluginManifestReadResult ReadFromJson(string json,
		string? versionDirectory,
		string expectedPluginId,
		string expectedVersion)
	{
		JsonReads.Add((json, versionDirectory, expectedPluginId, expectedVersion));

		return Default ?? PluginManifestReadResult.Fail(PluginManifestError.NotFound, "No fake result configured.");
	}

	public List<(string Json, string? VersionDirectory, string ExpectedPluginId, string ExpectedVersion)> JsonReads
	{
		get;
	} = [];
}

internal sealed class FakePluginInstallationCatalog : IPluginInstallationCatalog
{
	public List<InstalledPlugin> Plugins { get; } = [];

	public IReadOnlyList<InstalledPlugin> Discover() => Plugins;

	public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
	{
		version = Plugins.FirstOrDefault(p => p.PluginId == pluginId)?.ActiveVersion;
		return version is not null;
	}

	public int InvalidateCount { get; private set; }

	public void Invalidate() => InvalidateCount++;
}

internal sealed class FakePluginRuntimeStateStore : IPluginRuntimeStateStore
{
	public Dictionary<string, bool> States { get; } = new(StringComparer.Ordinal);

	public IReadOnlyDictionary<string, bool> Load() => States;

	public Task Save(string pluginId, bool started)
	{
		States[pluginId] = started;
		return Task.CompletedTask;
	}

	public Task Remove(string pluginId)
	{
		States.Remove(pluginId);
		return Task.CompletedTask;
	}
}

internal sealed class FakePluginProcessJournal : IPluginProcessJournal
{
	public List<PluginProcessJournalEntry> Entries { get; } = [];

	public PluginProcessJournalOwner? Owner { get; set; }

	public PluginProcessJournalSnapshot Load() => new()
	{
		Owner = Owner,
		Entries = Entries.ToList()
	};

	public Task Record(PluginProcessJournalEntry entry)
	{
		Entries.RemoveAll(existing => existing.LaunchId == entry.LaunchId);
		Entries.Add(entry);
		return Task.CompletedTask;
	}

	public Task Remove(string launchId) => Remove([launchId]);

	public Task Remove(IReadOnlyCollection<string> launchIds)
	{
		Entries.RemoveAll(entry => launchIds.Contains(entry.LaunchId));
		return Task.CompletedTask;
	}
}

internal sealed class FakeProcessTable : IProcessTable
{
	public Dictionary<int, RunningProcess> Processes { get; } = [];

	public List<int> KillTreeCalls { get; } = [];

	public HashSet<int> KillTreeThrowsFor { get; } = [];

	public void Seed(int processId, DateTimeOffset startedAt)
		=> Processes[processId] = new RunningProcess(processId, startedAt);

	public RunningProcess? TryGet(int processId)
		=> Processes.TryGetValue(processId, out var process) ? process : null;

	public Task KillTree(int processId, CancellationToken cancellationToken = default)
	{
		if (KillTreeThrowsFor.Contains(processId))
		{
			throw new InvalidOperationException($"Killing PID {processId} failed.");
		}

		KillTreeCalls.Add(processId);
		Processes.Remove(processId);
		return Task.CompletedTask;
	}
}

internal sealed class FakePluginProcess : IPluginProcess
{
	private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public int Id { get; init; } = 4242;

	public DateTimeOffset StartedAt { get; set; } = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

	public bool HasExited { get; private set; }

	public int? ExitCode { get; private set; }

	public Task<int> Exited => _exited.Task;

	public IReadOnlyList<string> BootstrapOutput { get; set; } = [];

	public int KillTreeCallCount { get; private set; }

	public bool ExitsOnKill { get; set; } = true;

	public bool DisposeCalled { get; private set; }

	public void CompleteExit(int exitCode)
	{
		if (HasExited)
		{
			return;
		}

		HasExited = true;
		ExitCode = exitCode;
		_exited.TrySetResult(exitCode);
	}

	public Task KillTree(CancellationToken cancellationToken = default)
	{
		KillTreeCallCount++;
		if (ExitsOnKill)
		{
			CompleteExit(-1);
		}

		return Task.CompletedTask;
	}

	public void Dispose() => DisposeCalled = true;
}

internal sealed class FakePluginProcessLauncher : IPluginProcessLauncher
{
	public List<PluginProcessStartRequest> Requests { get; } = [];

	public List<FakePluginProcess> StartedProcesses { get; } = [];

	public Func<PluginProcessStartRequest, IPluginProcess>? OnStart { get; set; }

	public Exception? ThrowOnStart { get; set; }

	public IPluginProcess Start(PluginProcessStartRequest request)
	{
		Requests.Add(request);

		if (ThrowOnStart is { } ex)
		{
			throw ex;
		}

		if (OnStart is { } factory)
		{
			return factory(request);
		}

		var process = new FakePluginProcess();
		StartedProcesses.Add(process);
		return process;
	}
}

internal sealed class FakePluginHealthProbe : IPluginHealthProbe
{
	public bool Result { get; set; } = true;

	public Func<int, string, bool>? OnProbe { get; set; }

	public List<(int Port, string Path)> Probes { get; } = [];

	public Task<bool> Probe(int port,
		string path,
		TimeSpan timeout,
		string pluginId,
		CancellationToken cancellationToken = default)
	{
		Probes.Add((port, path));
		return Task.FromResult(OnProbe?.Invoke(port, path) ?? Result);
	}
}

internal sealed class FakePluginSupervisor : IPluginSupervisor
{
	public List<PluginRuntimeSnapshot> SnapshotToReturn { get; } = [];

	public Dictionary<string, PluginSupervisorResult> StartResults { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, PluginSupervisorResult> StopResults { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, PluginSupervisorResult> RestartResults { get; } = new(StringComparer.Ordinal);

	public List<string> StartCalls { get; } = [];

	public List<(string PluginId, PluginStopReason Reason)> StopCalls { get; } = [];

	public List<string> RestartCalls { get; } = [];

	public List<string> ForgetCalls { get; } = [];

	public IReadOnlyList<PluginRuntimeSnapshot> Snapshot() => SnapshotToReturn;

	public Task<PluginSupervisorResult> Start(string pluginId, CancellationToken cancellationToken = default)
	{
		StartCalls.Add(pluginId);
		return Task.FromResult(StartResults.GetValueOrDefault(pluginId, PluginSupervisorResult.Ok()));
	}

	public Task<PluginSupervisorResult> Stop(string pluginId,
		PluginStopReason reason,
		CancellationToken cancellationToken = default)
	{
		StopCalls.Add((pluginId, reason));
		return Task.FromResult(StopResults.GetValueOrDefault(pluginId, PluginSupervisorResult.Ok()));
	}

	public Task<PluginSupervisorResult> Restart(string pluginId, CancellationToken cancellationToken = default)
	{
		RestartCalls.Add(pluginId);
		return Task.FromResult(RestartResults.GetValueOrDefault(pluginId, PluginSupervisorResult.Ok()));
	}

	public Task StopAll(PluginStopReason reason, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task Forget(string pluginId, CancellationToken cancellationToken = default)
	{
		ForgetCalls.Add(pluginId);
		return Task.CompletedTask;
	}

	public Task Reconcile(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class FakeDotnetMuxerLocator : IDotnetMuxerLocator
{
	public DotnetMuxer? Muxer { get; set; } = new()
	{
		ExecutablePath = OperatingSystem.IsWindows() ? @"C:\dotnet\dotnet.exe" : "/usr/share/dotnet/dotnet",
		InstalledRuntimeVersions = [new Version(10, 0, 0)]
	};

	public int InvalidateCount { get; private set; }

	public DotnetMuxer? Locate() => Muxer;

	public void Invalidate() => InvalidateCount++;
}
