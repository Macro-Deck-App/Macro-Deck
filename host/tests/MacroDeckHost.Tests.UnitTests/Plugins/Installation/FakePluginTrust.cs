using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

/// <summary>Reports the same canned verdict for every extracted tree, whether staged or already installed,
/// unless the test swaps <see cref="InstalledResult" />. Defaults to <see cref="PluginTrustVerdict.Trusted" />
/// so lifecycle tests that have nothing to do with signing (uninstall, enrollment, activation bookkeeping)
/// are not forced to also grant unsigned consent.</summary>
internal sealed class FakePluginTrustEvaluator : IPluginTrustEvaluator
{
	public PluginTrustResult InstalledResult { get; set; }
		= PluginTrustResult.Of(PluginTrustVerdict.Trusted, "cert-fake");

	public Task<PluginTrustResult> EvaluateInstalledAsync(string versionDirectory,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(InstalledResult);
}

internal sealed class InMemoryPluginTrustRecordRepository : IPluginTrustRecordRepository
{
	private readonly Dictionary<(string PluginId, string Version), PluginTrustRecordEntity> _records = new();

	public Task<PluginTrustRecordEntity?> GetVersion(string pluginId, string version)
	{
		_records.TryGetValue((pluginId, version), out var record);
		return Task.FromResult(record);
	}

	public Task<IReadOnlyList<PluginTrustRecordEntity>> GetForPlugin(string pluginId)
	{
		IReadOnlyList<PluginTrustRecordEntity> records = _records
			.Where(entry => entry.Key.PluginId == pluginId)
			.Select(entry => entry.Value)
			.ToList();

		return Task.FromResult(records);
	}

	public Task Upsert(string pluginId,
		string version,
		string admittedVerdict,
		string? certificateId,
		DateTime installedAt)
	{
		_records[(pluginId, version)] = new PluginTrustRecordEntity
		{
			Id = Guid.NewGuid(),
			PluginId = pluginId,
			Version = version,
			AdmittedVerdict = admittedVerdict,
			CertificateId = certificateId,
			InstalledAt = installedAt
		};

		return Task.CompletedTask;
	}

	public Task DeleteForPlugin(string pluginId)
	{
		foreach (var key in _records.Keys.Where(key => key.PluginId == pluginId).ToList())
		{
			_records.Remove(key);
		}

		return Task.CompletedTask;
	}

	public Task DeleteVersion(string pluginId, string version)
	{
		_records.Remove((pluginId, version));
		return Task.CompletedTask;
	}
}

internal sealed class FakePluginTrustBaseline : IPluginTrustBaseline
{
	public bool ExistsValue { get; set; }

	public Task<bool> Exists(CancellationToken cancellationToken = default) => Task.FromResult(ExistsValue);

	public Task Establish(CancellationToken cancellationToken = default)
	{
		ExistsValue = true;
		return Task.CompletedTask;
	}
}
