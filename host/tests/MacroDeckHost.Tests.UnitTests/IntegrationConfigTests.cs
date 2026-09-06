using System.Text.Json;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Infrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests;

public class IntegrationConfigTests
{
	private IntegrationConfig _config = null!;
	private IIntegrationConfigStore _store = null!;
	private IServiceScopeFactory _scopeFactory = null!;

	[SetUp]
	public void SetUp()
	{
		var services = new ServiceCollection();
		services.AddSingleton(new FakeSecretService());
		services.AddSingleton<ISecretService>(sp => sp.GetRequiredService<FakeSecretService>());
		services.AddSingleton<IIntegrationConfigStore>(sp =>
			new IntegrationConfigStore(new InMemoryConfigEntryRepository(),
				sp.GetRequiredService<FakeSecretService>()));
		var provider = services.BuildServiceProvider();
		_store = provider.GetRequiredService<IIntegrationConfigStore>();
		_scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
		_config = new IntegrationConfig("cf", _scopeFactory);
	}

	[Test]
	public void SetSecretAsync_throws_when_the_entry_no_longer_exists()
	{
		Assert.That(async () => await _config.SetSecretAsync(Guid.NewGuid(), "refreshToken", "value"),
			Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public void SetStringAsync_throws_when_the_entry_no_longer_exists()
	{
		Assert.That(async () => await _config.SetStringAsync(Guid.NewGuid(), "expiresAt", "value"),
			Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public async Task SetSecretAsync_stores_against_an_existing_entry()
	{
		var entryId = await _store.Create("cf",
			"Spotify",
			new Dictionary<string, JsonElement>());

		await _config.SetSecretAsync(entryId, "refreshToken", "rotated");

		Assert.That(await _config.GetSecretAsync(entryId, "refreshToken"), Is.EqualTo("rotated"));
	}

	[Test]
	public async Task GetStringAsync_returns_null_for_another_integrations_entry()
	{
		var otherEntryId = await _store.Create("other-integration",
			"Other",
			new Dictionary<string, JsonElement> { ["apiKey"] = JsonSerializer.SerializeToElement("not-yours") });

		Assert.That(await _config.GetStringAsync(otherEntryId, "apiKey"), Is.Null);
	}

	[Test]
	public async Task GetSecretAsync_returns_null_for_another_integrations_entry_even_when_a_key_matches()
	{
		var otherEntryId = await _store.Create("other-integration",
			"Other",
			new Dictionary<string, JsonElement>());
		await SetSecretDirectlyAsync(otherEntryId, "refreshToken", "someones-real-secret");

		Assert.That(await _config.GetSecretAsync(otherEntryId, "refreshToken"), Is.Null);
	}

	[Test]
	public void SetStringAsync_throws_for_another_integrations_entry_rather_than_writing_to_it()
	{
		Assert.That(async () =>
			{
				var otherEntryId
					= await _store.Create("other-integration", "Other", new Dictionary<string, JsonElement>());
				await _config.SetStringAsync(otherEntryId, "expiresAt", "hijacked");
			},
			Throws.InstanceOf<InvalidOperationException>());
	}

	[Test]
	public void SetSecretAsync_throws_for_another_integrations_entry_rather_than_writing_to_it()
	{
		Assert.That(async () =>
			{
				var otherEntryId
					= await _store.Create("other-integration", "Other", new Dictionary<string, JsonElement>());
				await _config.SetSecretAsync(otherEntryId, "refreshToken", "hijacked");
			},
			Throws.InstanceOf<InvalidOperationException>());
	}

	private async Task SetSecretDirectlyAsync(Guid entryId, string key, string value)
	{
		var ownerConfig = new IntegrationConfig("other-integration", _scopeFactory);
		await ownerConfig.SetSecretAsync(entryId, key, value);
	}

	private sealed class InMemoryConfigEntryRepository
		: MacroDeckHost.Application.Persistence.Repositories.IIntegrationConfigEntryRepository
	{
		private readonly List<Domain.Entities.IntegrationConfigEntryEntity> _entries = [];

		public Task<IReadOnlyList<Domain.Entities.IntegrationConfigEntryEntity>> GetByIntegrationId(
			string integrationId)
			=> Task.FromResult<IReadOnlyList<Domain.Entities.IntegrationConfigEntryEntity>>(_entries
				.Where(e => e.IntegrationId == integrationId)
				.ToList());

		public Task<Domain.Entities.IntegrationConfigEntryEntity?> GetById(Guid id)
			=> Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

		public Task Create(Domain.Entities.IntegrationConfigEntryEntity entry)
		{
			_entries.Add(entry);
			return Task.CompletedTask;
		}

		public Task Update(Domain.Entities.IntegrationConfigEntryEntity entry) => Task.CompletedTask;

		public Task TryDeleteById(Guid id)
		{
			_entries.RemoveAll(e => e.Id == id);
			return Task.CompletedTask;
		}
	}
}
