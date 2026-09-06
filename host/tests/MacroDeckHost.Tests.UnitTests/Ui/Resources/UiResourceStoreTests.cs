using System.Text;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Tests.UnitTests.Ui.Resources;

[TestFixture]
public class UiResourceStoreTests
{
	private static UiResourceRegistration Registration(string name, string content) => new()
	{
		OwnerId = "app.macro-deck.weather",
		Name = name,
		MediaType = "image/svg+xml",
		Content = Encoding.UTF8.GetBytes(content),
	};

	[Test]
	public void Identical_bytes_registered_twice_share_one_id_and_one_hash()
	{
		var store = new UiResourceStore();

		var first = store.Register(Registration("clear-day", "<svg/>"));
		var second = store.Register(Registration("clear-day", "<svg/>"));

		Assert.Multiple(() =>
		{
			Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(second.ContentHash, Is.EqualTo(first.ContentHash));
			Assert.That(first.ResourceId, Is.EqualTo("app.macro-deck.weather.clear-day"));
			Assert.That(first.ByteLength, Is.EqualTo(6));
		});
	}

	[Test]
	public void Re_registering_an_id_with_different_bytes_replaces_them_and_changes_the_hash()
	{
		var store = new UiResourceStore();

		var first = store.Register(Registration("clear-day", "<svg/>"));
		var second = store.Register(Registration("clear-day", "<svg id=\"x\"/>"));

		store.TryGet(first.ResourceId, out var served);

		Assert.Multiple(() =>
		{
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
			Assert.That(served.ContentHash, Is.EqualTo(second.ContentHash));
		});
	}

	[Test]
	public void Content_over_the_protocol_limit_is_refused()
	{
		var store = new UiResourceStore();

		var oversized = new UiResourceRegistration
		{
			OwnerId = "app.macro-deck.weather",
			Name = "huge",
			MediaType = "image/svg+xml",
			Content = new byte[ProtocolLimits.MaxUiResourceBytes + 1],
		};

		Assert.That(() => store.Register(oversized), Throws.ArgumentException);
	}

	[Test]
	public void An_owner_and_name_that_cannot_form_a_valid_identifier_are_refused()
	{
		var store = new UiResourceStore();

		Assert.That(() => store.Register(Registration("../escape", "<svg/>")), Throws.ArgumentException);
	}
}
