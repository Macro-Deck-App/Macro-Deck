using System.Text.Json;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests;

public class IntegrationConfigStoreTests
{
	private FakeSecretService _secretService = null!;
	private FakeConfigEntryRepository _repository = null!;
	private IntegrationConfigStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_secretService = new FakeSecretService();
		_repository = new FakeConfigEntryRepository();
		_store = new IntegrationConfigStore(_repository, _secretService);
	}

	[Test]
	public async Task Replace_keeps_the_entry_id_and_stores_the_new_title_and_values()
	{
		var entryId = await _store.Create("cf", "Old", Values(("clientId", JsonValue("old-client"))));

		var replaced = await _store.Replace(entryId, "New", Values(("clientId", JsonValue("new-client"))));

		Assert.That(replaced, Is.True);
		var record = await _store.Find(entryId);
		Assert.That(record, Is.Not.Null);
		Assert.That(record!.Title, Is.EqualTo("New"));
		Assert.That(record.Values["clientId"].GetString(), Is.EqualTo("new-client"));
	}

	[Test]
	public async Task Replace_deletes_secrets_the_new_values_no_longer_reference()
	{
		var oldSecret = await _secretService.Create("old-token", SecretKind.Secret);
		var entryId = await _store.Create("cf", "Spotify", Values(("accessToken", SecretRef(oldSecret))));

		var newSecret = await _secretService.Create("new-token", SecretKind.Secret);
		await _store.Replace(entryId, "Spotify", Values(("accessToken", SecretRef(newSecret))));

		// Re-authentication writes fresh secrets; the superseded ones must not linger in the store.
		Assert.That(await _secretService.Resolve(oldSecret), Is.Null);
		Assert.That(await _secretService.Resolve(newSecret), Is.EqualTo("new-token"));
	}

	[Test]
	public async Task Replace_keeps_a_secret_the_new_values_still_reference()
	{
		var secret = await _secretService.Create("client-secret", SecretKind.Secret);
		var entryId = await _store.Create("cf", "Spotify", Values(("clientSecret", SecretRef(secret))));

		await _store.Replace(entryId,
			"Spotify",
			Values(("clientSecret", SecretRef(secret)), ("accessToken", JsonValue("plain"))));

		Assert.That(await _secretService.Resolve(secret), Is.EqualTo("client-secret"));
	}

	[Test]
	public async Task Replace_reports_an_unknown_entry()
	{
		Assert.That(await _store.Replace(Guid.NewGuid(), "New", Values()), Is.False);
	}

	private static Dictionary<string, JsonElement> Values(params (string Key, JsonElement Value)[] pairs)
		=> pairs.ToDictionary(p => p.Key, p => p.Value);

	private static JsonElement SecretRef(Guid id)
		=> JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$secret"] = id.ToString() });

	private static JsonElement JsonValue(string value) => JsonSerializer.SerializeToElement(value);

	private sealed class FakeConfigEntryRepository : IIntegrationConfigEntryRepository
	{
		private readonly List<IntegrationConfigEntryEntity> _entries = [];

		public Task<IReadOnlyList<IntegrationConfigEntryEntity>> GetByIntegrationId(string integrationId)
			=> Task.FromResult<IReadOnlyList<IntegrationConfigEntryEntity>>(_entries
				.Where(e => e.IntegrationId == integrationId)
				.ToList());

		public Task<IntegrationConfigEntryEntity?> GetById(Guid id)
			=> Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

		public Task Create(IntegrationConfigEntryEntity entry)
		{
			_entries.Add(entry);
			return Task.CompletedTask;
		}

		public Task Update(IntegrationConfigEntryEntity entry) => Task.CompletedTask;

		public Task TryDeleteById(Guid id)
		{
			_entries.RemoveAll(e => e.Id == id);
			return Task.CompletedTask;
		}
	}
}
