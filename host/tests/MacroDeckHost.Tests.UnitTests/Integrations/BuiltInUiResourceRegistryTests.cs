using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class BuiltInUiResourceRegistryTests
{
	private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

	private UiResourceStore _store = null!;
	private BuiltInUiResourceRegistry _registry = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new UiResourceStore();
		_registry = new BuiltInUiResourceRegistry("app.macro-deck.example", _store);
	}

	[Test]
	public async Task A_registered_resource_is_owned_by_the_integration_and_can_be_served()
	{
		var handle = await _registry.RegisterAsync("cover", Jpeg, "image/jpeg");

		Assert.Multiple(() =>
		{
			Assert.That(handle.ResourceId, Is.EqualTo("app.macro-deck.example.cover"));
			Assert.That(_store.TryGet(handle.ResourceId, out var content), Is.True);
			Assert.That(content.MediaType, Is.EqualTo("image/jpeg"));
		});
	}

	[Test]
	public async Task A_built_in_may_register_its_own_vector_logo()
	{
		var handle = await _registry.RegisterAsync("logo", "<svg/>"u8.ToArray(), "image/svg+xml");

		Assert.That(handle.MediaType, Is.EqualTo("image/svg+xml"));
	}

	[Test]
	public async Task Registering_a_name_again_replaces_its_bytes_under_the_same_id()
	{
		var first = await _registry.RegisterAsync("cover", Jpeg, "image/jpeg");
		var second = await _registry.RegisterAsync("cover", new byte[] { 0xFF, 0xD8, 0xFF, 9 }, "image/jpeg");

		Assert.Multiple(() =>
		{
			Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
		});
	}

	[Test]
	public async Task A_removed_resource_is_no_longer_served()
	{
		var handle = await _registry.RegisterAsync("cover", Jpeg, "image/jpeg");

		await _registry.RemoveAsync("cover");

		Assert.That(_store.TryGet(handle.ResourceId, out _), Is.False);
	}

	[TestCase("bad name")]
	[TestCase("a.b")]
	[TestCase("")]
	public void An_invalid_name_is_refused(string name)
		=> Assert.ThrowsAsync<ArgumentException>(() => _registry.RegisterAsync(name, Jpeg, "image/jpeg"));

	[Test]
	public void An_unsupported_media_type_is_refused()
		=> Assert.ThrowsAsync<ArgumentException>(() => _registry.RegisterAsync("cover", Jpeg, "text/html"));

	[Test]
	public void Empty_content_is_refused()
		=> Assert.ThrowsAsync<ArgumentException>(() => _registry.RegisterAsync("cover", Array.Empty<byte>(), "image/jpeg"));
}
