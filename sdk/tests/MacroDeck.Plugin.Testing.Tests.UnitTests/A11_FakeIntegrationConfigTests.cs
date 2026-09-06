using MacroDeck.Plugin.Testing.Fakes;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A11 - <see cref="FakeIntegrationConfig" /> reproduces the config/secret contract including its nulls:
/// plain and secret values are separate namespaces, and a missing key is <c>null</c>, never an exception
/// or an empty string.
/// </summary>
[TestFixture]
public class A11_FakeIntegrationConfigTests
{
	[Test]
	public async Task Plain_and_secret_values_are_separate_namespaces_and_missing_keys_are_null()
	{
		var config = new FakeIntegrationConfig();
		var entryId = config.AddEntry("Test Entry");

		config.SeedString(entryId, "a", "plain-value");
		config.SeedSecret(entryId, "b", "secret-value");

		var stringForPlainKey = await config.GetStringAsync(entryId, "a");
		var stringForSecretOnlyKey = await config.GetStringAsync(entryId, "b");
		var secretForSecretKey = await config.GetSecretAsync(entryId, "b");
		var secretForPlainOnlyKey = await config.GetSecretAsync(entryId, "a");
		var missingString = await config.GetStringAsync(entryId, "missing");
		var missingSecret = await config.GetSecretAsync(entryId, "missing");
		var entries = await config.GetEntriesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(stringForPlainKey, Is.EqualTo("plain-value"));
			Assert.That(stringForSecretOnlyKey, Is.Null, "\"b\" is a secret; GetStringAsync must not see it");
			Assert.That(secretForSecretKey, Is.EqualTo("secret-value"));
			Assert.That(secretForPlainOnlyKey, Is.Null, "\"a\" is plain; GetSecretAsync must not see it");
			Assert.That(missingString, Is.Null);
			Assert.That(missingSecret, Is.Null);

			Assert.That(entries, Has.Count.EqualTo(1));
			Assert.That(entries[0].Id, Is.EqualTo(entryId));
			Assert.That(entries[0].Title, Is.EqualTo("Test Entry"));
		});
	}

	[Test]
	public void SetStringAsync_and_SetSecretAsync_throw_for_an_entry_that_was_never_seeded()
	{
		var config = new FakeIntegrationConfig();

		Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await config.SetStringAsync(Guid.NewGuid(), "key", "value"));

		Assert.ThrowsAsync<InvalidOperationException>(async ()
			=> await config.SetSecretAsync(Guid.NewGuid(), "key", "value"));
	}
}
