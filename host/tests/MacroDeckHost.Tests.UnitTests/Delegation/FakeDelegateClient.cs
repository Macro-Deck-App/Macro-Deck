using MacroDeck.Sdk.Scripts;
using MacroDeckHost.Integrations.Delegation.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

internal sealed class FakeDelegateClient : IDelegateClient
{
	private readonly Lock _lock = new();
	private readonly Dictionary<string, RemoteConfig> _remotes = new(StringComparer.Ordinal);

	public List<(string Call, string BaseUrl)> Calls { get; } = [];

	public int LoginCount => CallCount("login", null);

	public int CallCount(string call, Uri? baseUrl)
	{
		lock (_lock)
		{
			return Calls.Count(c => c.Call == call && (baseUrl is null || c.BaseUrl == baseUrl.ToString()));
		}
	}

	public RemoteConfig For(Uri baseUrl)
	{
		lock (_lock)
		{
			if (!_remotes.TryGetValue(baseUrl.ToString(), out var config))
			{
				config = new RemoteConfig();
				_remotes[baseUrl.ToString()] = config;
			}

			return config;
		}
	}

	public Task ProbeAsync(Uri baseUrl, CancellationToken cancellationToken)
	{
		Record("probe", baseUrl);
		var config = For(baseUrl);
		if (config.ProbeException is { } ex)
		{
			return Task.FromException(ex);
		}

		return config.Reachable ? Task.CompletedTask : Task.FromException(new DelegateUnreachableException());
	}

	public Task<DelegateLoginResult> LoginAsync(Uri baseUrl,
		string username,
		string password,
		CancellationToken cancellationToken)
	{
		Record("login", baseUrl);
		var config = For(baseUrl);

		if (config.LoginException is { } ex)
		{
			return Task.FromException<DelegateLoginResult>(ex);
		}

		if (!string.Equals(username, config.Username, StringComparison.Ordinal) ||
			!string.Equals(password, config.Password, StringComparison.Ordinal))
		{
			return Task.FromException<DelegateLoginResult>(new DelegateUnauthorizedException());
		}

		return Task.FromResult(new DelegateLoginResult($"token-{Guid.NewGuid():N}",
			TimeSpan.FromSeconds(config.TokenExpiresInSeconds)));
	}

	public Task<DelegateConnectionInfo> GetConnectionInfoAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken)
	{
		Record("connection-info", baseUrl);
		var config = For(baseUrl);
		return config.ConnectionInfoException is { } ex
			? Task.FromException<DelegateConnectionInfo>(ex)
			: Task.FromResult(new DelegateConnectionInfo(config.InstanceName));
	}

	public Task<IReadOnlyList<DelegateScriptSummary>> GetScriptsAsync(Uri baseUrl,
		string token,
		CancellationToken cancellationToken)
	{
		Record("scripts", baseUrl);
		var config = For(baseUrl);
		if (config.ScriptsException is { } ex)
		{
			return Task.FromException<IReadOnlyList<DelegateScriptSummary>>(ex);
		}

		IReadOnlyList<DelegateScriptSummary> scripts = config.Scripts
			.Select(pair => new DelegateScriptSummary(pair.Key,
				pair.Value,
				config.ScriptInputs.GetValueOrDefault(pair.Key, []),
				config.ScriptRunsOnWidget.GetValueOrDefault(pair.Key)))
			.ToList();
		return Task.FromResult(scripts);
	}

	public async Task<DelegateRunResult> RunScriptAsync(Uri baseUrl,
		string token,
		string scriptId,
		string? clientId,
		int callDepth,
		IReadOnlyDictionary<string, object?>? inputs,
		CancellationToken cancellationToken)
	{
		Record("run", baseUrl);
		var config = For(baseUrl);
		lock (_lock)
		{
			config.RunCallDepths.Add(callDepth);
			config.RunScriptIds.Add(scriptId);
			config.RunInputs.Add(inputs);
		}

		if (config.HangRun)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
		}

		if (config.RunException is { } ex)
		{
			throw ex;
		}

		return config.RunHandler?.Invoke(scriptId) ?? new DelegateRunResult(true, null, "Succeeded");
	}

	public void Dispose()
	{
	}

	private void Record(string call, Uri baseUrl)
	{
		lock (_lock)
		{
			Calls.Add((call, baseUrl.ToString()));
		}
	}

	internal sealed class RemoteConfig
	{
		public bool Reachable { get; set; } = true;

		public Exception? ProbeException { get; set; }

		public string Username { get; set; } = "admin";

		public string Password { get; set; } = "correct-password";

		public int TokenExpiresInSeconds { get; set; } = 3600;

		public Exception? LoginException { get; set; }

		public string InstanceName { get; set; } = "OTHER-PC";

		public Exception? ConnectionInfoException { get; set; }

		public Dictionary<string, string> Scripts { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, IReadOnlyList<ScriptInput>> ScriptInputs { get; } = new(StringComparer.Ordinal);

		public Dictionary<string, bool> ScriptRunsOnWidget { get; } = new(StringComparer.Ordinal);

		public Exception? ScriptsException { get; set; }

		public bool HangRun { get; set; }

		public Exception? RunException { get; set; }

		public Func<string, DelegateRunResult>? RunHandler { get; set; }

		public List<int> RunCallDepths { get; } = [];

		public List<string> RunScriptIds { get; } = [];

		public List<IReadOnlyDictionary<string, object?>?> RunInputs { get; } = [];
	}
}
