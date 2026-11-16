using System.Collections.Concurrent;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public class McpNetworkAccessTests
{
	private MemoryPreferences _preferences = null!;
	private List<McpAccessTokenEntity> _tokens = null!;
	private McpNetworkAccess _access = null!;

	[SetUp]
	public void SetUp()
	{
		_preferences = new MemoryPreferences();
		_tokens = [];
		var services = new ServiceCollection()
			.AddSingleton<IAppPreferenceRepository>(_preferences)
			.AddSingleton<IMcpAccessTokenRepository>(new MemoryTokens(_tokens))
			.BuildServiceProvider();
		_access = new McpNetworkAccess(services.GetRequiredService<IServiceScopeFactory>(), new NoConnections());
	}

	[Test]
	public async Task A_new_install_starts_with_network_access_off_and_keeps_it_off_once_a_token_exists()
	{
		await _access.Seed();
		_tokens.Add(Token(null));
		await _access.Seed();

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_access.Allowed, Is.False);
			Assert.That(_preferences.Values[McpNetworkAccess.PreferenceKey], Is.EqualTo("False"));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_install_that_already_has_a_live_token_keeps_serving_the_network()
	{
		_tokens.Add(Token(DateTime.UtcNow));
		_tokens.Add(Token(null));

		await _access.Seed();

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_access.Allowed, Is.True);
			Assert.That(_preferences.Values[McpNetworkAccess.PreferenceKey], Is.EqualTo("True"));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task An_install_with_only_revoked_tokens_starts_with_network_access_off()
	{
		_tokens.Add(Token(DateTime.UtcNow));

		await _access.Seed();

		Assert.That(_access.Allowed, Is.False);
	}

	[Test]
	public async Task A_stored_choice_is_kept_and_a_change_is_stored()
	{
		_preferences.Values[McpNetworkAccess.PreferenceKey] = "True";
		await _access.Seed();
		var seeded = _access.Allowed;

		await _access.Change(false);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(seeded, Is.True);
			Assert.That(_access.Allowed, Is.False);
			Assert.That(_preferences.Values[McpNetworkAccess.PreferenceKey], Is.EqualTo("False"));
			return Task.CompletedTask;
		});
	}

	private static McpAccessTokenEntity Token(DateTime? revokedAt)
	{
		return new McpAccessTokenEntity
			{ Name = "client", TokenHash = Guid.NewGuid().ToString("N"), RevokedAt = revokedAt };
	}

	private sealed class MemoryPreferences : IAppPreferenceRepository
	{
		public ConcurrentDictionary<string, string> Values { get; } = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
		{
			return Task.FromResult(Values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);
		}

		public Task SetValue(string key, string value)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}
	}

	private sealed class MemoryTokens : IMcpAccessTokenRepository
	{
		private readonly List<McpAccessTokenEntity> _tokens;

		public MemoryTokens(List<McpAccessTokenEntity> tokens)
		{
			_tokens = tokens;
		}

		public Task<IReadOnlyList<McpAccessTokenEntity>> GetAll()
		{
			return Task.FromResult<IReadOnlyList<McpAccessTokenEntity>>(_tokens.ToList());
		}

		public Task<McpAccessTokenEntity?> GetById(Guid id)
		{
			throw new NotSupportedException();
		}

		public Task<McpAccessTokenEntity?> GetByHash(string tokenHash)
		{
			throw new NotSupportedException();
		}

		public Task Create(McpAccessTokenEntity token)
		{
			throw new NotSupportedException();
		}

		public Task TouchLastUsed(Guid id, DateTime at)
		{
			throw new NotSupportedException();
		}

		public Task Revoke(Guid id, DateTime at)
		{
			throw new NotSupportedException();
		}

		public Task Delete(Guid id)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class NoConnections : IMcpConnections
	{
		public IReadOnlyList<McpConnection> Snapshot()
		{
			return [];
		}

		public int TerminateForToken(Guid tokenId)
		{
			return 0;
		}

		public int TerminateRemote()
		{
			return 0;
		}
	}
}
