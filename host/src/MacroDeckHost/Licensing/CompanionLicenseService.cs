using System.Text.Json;
using MacroDeckHost.Application.Licensing;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Infrastructure.Licensing;
using MacroDeckHost.Integrations;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Licensing;

public sealed class CompanionLicenseService : ICompanionLicenseService, IDisposable
{
	public const string TokenKey = "license.companionToken";
	public const string TrialsKey = "license.trials";
	public const int MaximumTrials = 1000;
	public const string RevokedTestIdsKey = "license.revokedTestIds";
	public const int MaximumRevokedTestIds = 100;
	private const int MaximumTrialDeviceIdLength = 128;

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IPlatformLicenseClient _platform;
	private readonly CompanionLicenseTokens _tokens;
	private readonly CompanionDeviceRegistry _companions;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _exchangeGate = new();

	public CompanionLicenseService(IServiceScopeFactory scopeFactory,
		IPlatformLicenseClient platform,
		CompanionLicenseTokens tokens,
		CompanionDeviceRegistry companions,
		TimeProvider time,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_platform = platform;
		_tokens = tokens;
		_companions = companions;
		_time = time;
		_logger = logger;
	}

	internal Task Exchange { get; private set; } = Task.CompletedTask;

	public async Task<SyncCompanionLicenseResponse> SyncAsync(string connectionId,
		SyncCompanionLicenseRequest request,
		CancellationToken cancellationToken)
	{
		var license = await AdoptAsync(request.License, cancellationToken);
		var trialStartedAt = await TrialStartAsync(request.TrialDeviceId, request.TrialStarted, cancellationToken);
		if (license is null or { IsTest: true } && request.Proof is { } proof)
		{
			lock (_exchangeGate)
			{
				if (Exchange.IsCompleted)
				{
					Exchange = Task.Run(() => ExchangeAsync(connectionId, proof), CancellationToken.None);
				}
			}
		}

		return new SyncCompanionLicenseResponse
		{
			License = license?.Token,
			TrialStartedAt = trialStartedAt,
			RevokedLicenseIds = await RevokedTestIdsAsync(cancellationToken)
		};
	}

	public async Task<CompanionLicenseStatus> RevokeTestLicenseAsync(CancellationToken cancellationToken)
	{
		string? revokedId = null;
		await using (var scope = _scopeFactory.CreateAsyncScope())
		{
			var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
			await _gate.WaitAsync(cancellationToken);
			try
			{
				var stored = await _tokens.VerifyAsync((await preferences.GetByKey(TokenKey))?.Value, trustTestKey: true);
				if (stored is { IsTest: true })
				{
					var revoked = await ReadRevokedAsync(preferences);
					revoked.Remove(stored.LicenseId);
					revoked.Add(stored.LicenseId);
					await preferences.SetValue(TokenKey, string.Empty);
					await preferences.SetValue(RevokedTestIdsKey,
						JsonSerializer.Serialize(revoked.TakeLast(MaximumRevokedTestIds)));
					revokedId = stored.LicenseId;
				}
			}
			finally
			{
				_gate.Release();
			}
		}

		if (revokedId is not null)
		{
			await _companions.SendLicenseRevokedAsync(new CompanionLicenseRevokedEvent { LicenseId = revokedId });
		}

		return await GetStatusAsync(cancellationToken);
	}

	private async Task<List<string>> RevokedTestIdsAsync(CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await ReadRevokedAsync(preferences);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async Task<List<string>> ReadRevokedAsync(IAppPreferenceRepository preferences)
	{
		var json = (await preferences.GetByKey(RevokedTestIdsKey))?.Value;
		if (string.IsNullOrEmpty(json))
		{
			return [];
		}

		try
		{
			return JsonSerializer.Deserialize<List<string>>(json) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	public async Task<CompanionLicenseStatus> GetStatusAsync(CancellationToken cancellationToken)
	{
		var status = Status(await AdoptAsync(null, cancellationToken));
		status.TestLicenseStored = await TestLicenseStoredAsync(cancellationToken);
		return status;
	}

	// Checked with the test key trusted: debug Companions keep trusting a test license whatever the host's
	// mode, so it has to stay revocable after developer mode is turned off.
	private async Task<bool> TestLicenseStoredAsync(CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await _tokens.VerifyAsync((await preferences.GetByKey(TokenKey))?.Value, trustTestKey: true) is
				{ IsTest: true };
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<CompanionLicenseStatus?> IssueTestLicenseAsync(CancellationToken cancellationToken)
	{
		await using (var scope = _scopeFactory.CreateAsyncScope())
		{
			if (!await DeveloperModeAsync(scope.ServiceProvider))
			{
				return null;
			}
		}

		var issued = await _platform.IssueCompanionLicenseAsync(
			new CompanionLicenseProof
				{ Platform = FakePlatformLicenseClient.TestSource, ProductId = CompanionLicenseTokens.Product },
			cancellationToken);
		var license = await AdoptAsync(issued, cancellationToken);
		if (license is not null)
		{
			await _companions.SendLicenseAsync(new CompanionLicenseEvent { License = license.Token }, null);
		}

		var status = Status(license);
		status.TestLicenseStored = license?.IsTest == true;
		return status;
	}

	private async Task ExchangeAsync(string connectionId, CompanionLicenseProof proof)
	{
		try
		{
			var issued = await _platform.IssueCompanionLicenseAsync(proof, CancellationToken.None);
			if (issued is null)
			{
				return;
			}

			var license = await AdoptAsync(issued, CancellationToken.None);
			if (license is null)
			{
				_logger.Warning("The platform issued a Companion license this host does not trust");
				return;
			}

			await _companions.SendLicenseAsync(new CompanionLicenseEvent { License = license.Token }, connectionId);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Exchanging a Companion purchase proof for a license failed");
		}
	}

	// The stored token is verified against the current trust set on every read, so a test license
	// stops being handed out as soon as developer mode is turned off.
	private async Task<CompanionLicense?> AdoptAsync(string? candidate, CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var developerMode = await DeveloperModeAsync(scope.ServiceProvider);
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var stored = await _tokens.VerifyAsync((await preferences.GetByKey(TokenKey))?.Value, developerMode);
			var adopted = await _tokens.VerifyAsync(candidate, developerMode);
			if (adopted is null ||
				stored is not null && (!stored.IsTest || adopted.IsTest) ||
				(await ReadRevokedAsync(preferences)).Contains(adopted.LicenseId))
			{
				return stored;
			}

			await preferences.SetValue(TokenKey, adopted.Token);
			return adopted;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<long?> TrialStartAsync(string? trialDeviceId,
		bool trialStarted,
		CancellationToken cancellationToken)
	{
		if (trialDeviceId is not { Length: > 0 and <= MaximumTrialDeviceIdLength })
		{
			return null;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var trials = ReadTrials((await preferences.GetByKey(TrialsKey))?.Value);
			if (trials.TryGetValue(trialDeviceId, out var known))
			{
				return known;
			}

			if (!trialStarted)
			{
				return null;
			}

			var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
			trials[trialDeviceId] = now;
			// A ceiling on growth, not abuse protection: the oldest start goes first. A central trial
			// record belongs to the Platform API once it exists.
			foreach (var oldest in trials.OrderBy(entry => entry.Value)
				.Take(trials.Count - MaximumTrials)
				.Select(entry => entry.Key)
				.ToList())
			{
				trials.Remove(oldest);
			}

			await preferences.SetValue(TrialsKey, JsonSerializer.Serialize(trials));
			return now;
		}
		finally
		{
			_gate.Release();
		}
	}

	private static Dictionary<string, long> ReadTrials(string? json)
	{
		if (string.IsNullOrEmpty(json))
		{
			return new Dictionary<string, long>(StringComparer.Ordinal);
		}

		try
		{
			return new Dictionary<string, long>(JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? [],
				StringComparer.Ordinal);
		}
		catch (JsonException)
		{
			return new Dictionary<string, long>(StringComparer.Ordinal);
		}
	}

	public void Dispose() => _gate.Dispose();

	private static async Task<bool> DeveloperModeAsync(IServiceProvider services)
		=> (await services.GetRequiredService<IAppPreferenceService>().GetDeveloper()).Enabled;

	private static CompanionLicenseStatus Status(CompanionLicense? license)
		=> license is null
			? new CompanionLicenseStatus()
			: new CompanionLicenseStatus
			{
				Licensed = true,
				LicenseId = license.LicenseId,
				Source = license.Source,
				KeyId = license.KeyId,
				IssuedAt = license.IssuedAt.ToUnixTimeMilliseconds(),
				IsTest = license.IsTest
			};
}
