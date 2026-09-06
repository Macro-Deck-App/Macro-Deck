using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

public sealed class PluginTrustBaseline : IPluginTrustBaseline
{
	public const string EstablishedAtKey = "plugins.trust.baselineEstablishedAt";

	private readonly IAppPreferenceRepository _repository;
	private readonly TimeProvider _timeProvider;

	public PluginTrustBaseline(IAppPreferenceRepository repository, TimeProvider timeProvider)
	{
		_repository = repository;
		_timeProvider = timeProvider;
	}

	public async Task<bool> Exists(CancellationToken cancellationToken = default)
		=> (await _repository.GetByKey(EstablishedAtKey)) is not null;

	public Task Establish(CancellationToken cancellationToken = default)
		=> _repository.SetValue(EstablishedAtKey, _timeProvider.GetUtcNow().UtcDateTime.ToString("O"));
}
