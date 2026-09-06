using System.Text;
using System.Text.Json;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public sealed class WebSocketUiTransportTests
{
	private static readonly string[] AllAndGroup = ["all", "group"];
	private static readonly string[] AllAndDirect = ["all", "direct"];
	private static readonly string[] CurrentOnly = ["current"];

	[Test]
	public async Task Broadcast_group_and_connection_targeting_preserve_the_existing_transport_semantics()
	{
		var transport = new WebSocketUiTransport();
		var a = new List<UiWebSocketEnvelope>();
		var b = new List<UiWebSocketEnvelope>();
		transport.Add("a", Record(a));
		transport.Add("b", Record(b));
		await transport.AddToGroup("a", "group");

		await transport.Send(new Notice("all"));
		await transport.SendToGroup("group", new Notice("group"));
		await transport.SendToConnection("b", new Notice("direct"));

		Assert.Multiple(() =>
		{
			Assert.That(Payloads(a), Is.EqualTo(AllAndGroup));
			Assert.That(Payloads(b), Is.EqualTo(AllAndDirect));
			Assert.That(a.Concat(b).All(message => message is { Kind: "message", Type: nameof(Notice) }), Is.True);
		});
	}

	[Test]
	public async Task Removed_or_unknown_connections_cannot_leave_zombie_group_memberships()
	{
		var transport = new WebSocketUiTransport();
		var first = new List<UiWebSocketEnvelope>();
		var replacement = new List<UiWebSocketEnvelope>();

		await transport.AddToGroup("connection", "before-add");
		transport.Add("connection", Record(first));
		await transport.SendToGroup("before-add", new Notice("not-delivered"));

		await transport.AddToGroup("connection", "active");
		transport.Remove("connection");
		transport.Add("connection", Record(replacement));
		await transport.SendToGroup("active", new Notice("also-not-delivered"));

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.Empty);
			Assert.That(replacement, Is.Empty);
		});
	}

	[Test]
	public async Task An_empty_group_can_be_reused_without_reviving_old_members()
	{
		var transport = new WebSocketUiTransport();
		var oldMember = new List<UiWebSocketEnvelope>();
		var newMember = new List<UiWebSocketEnvelope>();
		transport.Add("old", Record(oldMember));
		transport.Add("new", Record(newMember));
		await transport.AddToGroup("old", "group");
		await transport.RemoveFromGroup("old", "group");
		await transport.AddToGroup("new", "group");

		await transport.SendToGroup("group", new Notice("current"));

		Assert.Multiple(() =>
		{
			Assert.That(oldMember, Is.Empty);
			Assert.That(Payloads(newMember), Is.EqualTo(CurrentOnly));
		});
	}

	[Test]
	public async Task A_raw_ui_session_tree_is_written_once_without_reencoding_its_json()
	{
		const string rawTree = """{"root":{"label":"caf\u00e9","size":1e+02}}""";
		var transport = new WebSocketUiTransport();
		var messages = new List<UiWebSocketEnvelope>();
		transport.Add("connection", Record(messages));

		await transport.SendToConnection("connection",
			new UiSessionTreeUpdatedEvent
			{
				SessionId = "session-1",
				Revision = 1,
				Tree = UiRawJson.FromUtf8(Encoding.UTF8.GetBytes(rawTree))
			});

		var wire = JsonSerializer.Serialize(messages.Single(), UiWebSocketProtocol.Json);
		using var document = JsonDocument.Parse(wire);
		var tree = document.RootElement.GetProperty("payload").GetProperty("tree");
		Assert.Multiple(() =>
		{
			Assert.That(tree.ValueKind, Is.EqualTo(JsonValueKind.Object));
			Assert.That(tree.GetProperty("root").GetProperty("size").GetDouble(), Is.EqualTo(100));
			Assert.That(wire, Does.Contain($"\"tree\":{rawTree}"));
		});
	}

	private static Func<UiWebSocketEnvelope, CancellationToken, ValueTask<bool>> Record(
		List<UiWebSocketEnvelope> messages)
		=> (message, _) =>
		{
			messages.Add(message);
			return ValueTask.FromResult(true);
		};

	private static string[] Payloads(IEnumerable<UiWebSocketEnvelope> messages)
		=> [.. messages.Select(message => ((Notice)message.Payload!).Value)];

	private sealed record Notice(string Value);
}
