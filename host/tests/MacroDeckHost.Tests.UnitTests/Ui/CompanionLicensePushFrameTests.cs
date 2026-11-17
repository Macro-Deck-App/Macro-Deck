using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Ui.Transport.Messages.Licensing;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public sealed class CompanionLicensePushFrameTests
{
	[Test]
	public async Task A_license_the_host_received_reaches_the_companion_in_the_frozen_frame()
	{
		var frame = await FrameAsync(new CompanionLicenseEvent { License = "license-token" });

		Assert.Multiple(() =>
		{
			AssertPush(frame, "CompanionLicenseEvent");
			Assert.That((string?)frame["payload"]?["license"], Is.EqualTo("license-token"));
		});
	}

	[Test]
	public async Task A_revoked_license_reaches_the_companion_in_the_frozen_frame()
	{
		var frame = await FrameAsync(new CompanionLicenseRevokedEvent { LicenseId = "license-1" });

		Assert.Multiple(() =>
		{
			AssertPush(frame, "CompanionLicenseRevokedEvent");
			Assert.That((string?)frame["payload"]?["licenseId"], Is.EqualTo("license-1"));
		});
	}

	private static void AssertPush(JsonNode frame, string type)
	{
		Assert.That((int?)frame["protocolVersion"], Is.EqualTo(1));
		Assert.That((string?)frame["kind"], Is.EqualTo("message"));
		Assert.That((string?)frame["type"], Is.EqualTo(type));
	}

	private static async Task<JsonNode> FrameAsync<T>(T message)
		where T : class
	{
		var transport = new WebSocketUiTransport();
		var sent = new List<UiWebSocketEnvelope>();
		transport.Add("companion",
			(envelope, _) =>
			{
				sent.Add(envelope);
				return ValueTask.FromResult(true);
			});

		await transport.SendToConnection("companion", message);

		return JsonNode.Parse(JsonSerializer.Serialize(sent.Single(), UiWebSocketProtocol.Json))!;
	}
}
