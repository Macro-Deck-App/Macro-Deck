using System.Text.Json;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Resources;

[TestFixture]
public class UiResourceTests
{
	[Test]
	public void A_minimal_resource_handle_omits_its_absent_members()
	{
		var resource = new UiResource { ResourceId = "icon-1" };

		Assert.That(UiCanonicalJson.Serialize(resource), Is.EqualTo("""{"resourceId":"icon-1"}"""));
	}

	[Test]
	public void A_full_resource_handle_writes_members_in_declared_order()
	{
		var resource = new UiResource
		{
			ResourceId = "icon-1", ContentHash = "sha256:0a1b", MediaType = "image/png", ByteLength = 2048,
		};

		Assert.That(UiCanonicalJson.Serialize(resource),
			Is.EqualTo(
				"""{"resourceId":"icon-1","contentHash":"sha256:0a1b","mediaType":"image/png","byteLength":2048}"""));
	}

	[TestCase("not-a-hash")]
	[TestCase("SHA256:ABCDEF")]
	[TestCase("sha256:")]
	[TestCase("")]
	public void The_model_does_not_validate_or_normalize_the_content_hash(string hash)
	{
		var resource = new UiResource { ResourceId = "icon-1", ContentHash = hash };

		var json = UiCanonicalJson.Serialize(resource);
		var roundTripped = JsonSerializer.Deserialize<UiResource>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(roundTripped.ContentHash, Is.EqualTo(hash));
			Assert.That(UiCanonicalJson.Serialize(roundTripped), Is.EqualTo(json));
		});
	}

	[Test]
	public void A_resource_handle_carries_no_payload_member()
	{
		var forbiddenNames = new[] { "Data", "Content", "Bytes" };

		var properties = typeof(UiResource).GetProperties();

		Assert.Multiple(() =>
		{
			foreach (var property in properties)
			{
				Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(byte[])));
				Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(Stream)));
				Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(Memory<byte>)));
				Assert.That(forbiddenNames, Does.Not.Contain(property.Name));
			}
		});
	}
}
