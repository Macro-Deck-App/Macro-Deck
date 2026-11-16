using MacroDeckHost.Application.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Mcp;

public interface IMcpNetworkAccess
{
	bool Allowed { get; }

	Task Seed();

	Task<bool> Change(bool allowed);
}

public sealed class McpNetworkAccess : IMcpNetworkAccess
{
	public const string PreferenceKey = "mcp.networkClientsAllowed";

	private readonly IServiceScopeFactory _scopes;
	private readonly IMcpConnections _connections;
	private volatile bool _allowed;

	public McpNetworkAccess(IServiceScopeFactory scopes, IMcpConnections connections)
	{
		_scopes = scopes;
		_connections = connections;
	}

	public bool Allowed => _allowed;

	public async Task Seed()
	{
		using var scope = _scopes.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var stored = await preferences.GetByKey(PreferenceKey);
		if (stored is not null)
		{
			_allowed = bool.TryParse(stored.Value, out var allowed) && allowed;
			return;
		}

		var tokens = await scope.ServiceProvider.GetRequiredService<IMcpAccessTokenRepository>().GetAll();
		var seeded = tokens.Any(token => token.RevokedAt is null);
		await preferences.SetValue(PreferenceKey, seeded.ToString());
		_allowed = seeded;
	}

	public async Task<bool> Change(bool allowed)
	{
		using (var scope = _scopes.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>()
				.SetValue(PreferenceKey, allowed.ToString());
		}

		_allowed = allowed;
		if (!allowed)
		{
			_connections.TerminateRemote();
		}

		return allowed;
	}
}
