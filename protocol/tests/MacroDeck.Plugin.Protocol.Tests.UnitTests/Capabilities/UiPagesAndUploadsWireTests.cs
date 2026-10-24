using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class UiPagesAndUploadsWireTests
{
	[Test]
	public void A_describe_from_an_sdk_that_predates_pages_reads_as_no_pages()
	{
		var described = JsonSerializer.Deserialize<UiDescribePayload>(
			"""{"surfaces":[{"kind":"widget","sessionMode":"shared"}],"uiModelVersion":4}""",
			PluginProtocolJson.Options)!;

		Assert.That(described.Pages, Is.Null);
	}

	[Test]
	public void Declared_pages_travel_under_their_published_keys()
	{
		var json = JsonSerializer.SerializeToElement(new UiDescribePayload
			{
				Surfaces = [new UiSurfaceDescriptorDto { Kind = "page", SessionMode = "exclusive" }],
				UiModelVersion = 4,
				Pages = [new UiPageDescriptorDto { Id = "scripts", Title = "Scripts", IconName = "file-code" }]
			},
			PluginProtocolJson.Options);

		var page = json.GetProperty("pages")[0];

		Assert.Multiple(() =>
		{
			Assert.That(page.GetProperty("id").GetString(), Is.EqualTo("scripts"));
			Assert.That(page.GetProperty("iconName").GetString(), Is.EqualTo("file-code"));
			Assert.That(JsonSerializer.Deserialize<UiDescribePayload>(json, PluginProtocolJson.Options)!.Pages![0]
					.Title,
				Is.EqualTo((LocalizedText)"Scripts"));
		});
	}

	[Test]
	public void A_read_upload_call_and_its_answer_travel_under_their_published_keys()
	{
		var arguments = JsonSerializer.SerializeToElement(
			new UiOriginReadUploadArguments { SessionId = "s1", UploadId = "u1" },
			PluginProtocolJson.Options);
		var answer = JsonSerializer.Deserialize<UiOriginReadUploadResult>(
			"""{"found":true,"byteLength":3,"contentHash":"sha256:ab","transferId":"t1"}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(arguments.GetProperty("sessionId").GetString(), Is.EqualTo("s1"));
			Assert.That(arguments.GetProperty("uploadId").GetString(), Is.EqualTo("u1"));
			Assert.That(answer,
				Is.EqualTo(new UiOriginReadUploadResult
				{
					Found = true, ByteLength = 3, ContentHash = "sha256:ab", TransferId = "t1"
				}));
		});
	}
}
