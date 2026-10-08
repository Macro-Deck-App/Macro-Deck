using MacroDeckHost.Application.Persistence.Repositories;

namespace MacroDeckHost.Application.Network.Http;

public record HttpSettings(string? CustomUserAgent, string EffectiveUserAgent, string DefaultUserAgent);

public record HttpSettingsUpdate(bool Success, HttpSettings Settings);

public interface IHttpSettingsService
{
	Task<HttpSettings> Load();

	Task<HttpSettingsUpdate> SetUserAgent(string? userAgent);

	Task Seed();
}

public sealed class HttpSettingsService : IHttpSettingsService
{
	public const string UserAgentKey = "http.userAgent";

	private readonly IAppPreferenceRepository _repository;
	private readonly HttpUserAgentState _state;

	public HttpSettingsService(IAppPreferenceRepository repository, HttpUserAgentState state)
	{
		_repository = repository;
		_state = state;
	}

	public async Task<HttpSettings> Load()
	{
		var stored = (await _repository.GetByKey(UserAgentKey))?.Value;
		return Build(HttpUserAgent.TryNormalize(stored, out var custom) ? custom : null);
	}

	public async Task<HttpSettingsUpdate> SetUserAgent(string? userAgent)
	{
		if (string.IsNullOrWhiteSpace(userAgent))
		{
			await _repository.SetValue(UserAgentKey, string.Empty);
			_state.Apply(null);
			return new HttpSettingsUpdate(true, Build(null));
		}

		if (!HttpUserAgent.TryNormalize(userAgent, out var normalized))
		{
			return new HttpSettingsUpdate(false, await Load());
		}

		await _repository.SetValue(UserAgentKey, normalized);
		_state.Apply(normalized);
		return new HttpSettingsUpdate(true, Build(normalized));
	}

	public async Task Seed()
	{
		_state.Apply((await Load()).CustomUserAgent);
	}

	private static HttpSettings Build(string? custom)
		=> new(custom, custom ?? HttpUserAgent.Default, HttpUserAgent.Default);
}
