using System.Text.Json;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsRpcFramesTests
{
	[Test]
	public void Request_MatchesTheDocumentedShape()
	{
		var json = StreamlabsRpcFrames.Request(1, "ScenesService", "makeSceneActive", ["scene_1"]);

		Assert.That(json,
			Is.EqualTo(
				"""{"jsonrpc":"2.0","id":1,"method":"makeSceneActive","params":{"resource":"ScenesService","args":["scene_1"]}}"""));
	}

	[Test]
	public void Auth_TargetsTcpServerService()
	{
		var json = StreamlabsRpcFrames.Auth(8, "secret");

		Assert.That(json,
			Is.EqualTo(
				"""{"jsonrpc":"2.0","id":8,"method":"auth","params":{"resource":"TcpServerService","args":["secret"]}}"""));
	}

	[Test]
	public void Subscribe_CallsTheObservableAsAMethod()
	{
		var json = StreamlabsRpcFrames.Subscribe(3, "ScenesService", "sceneSwitched");

		Assert.That(json,
			Is.EqualTo(
				"""{"jsonrpc":"2.0","id":3,"method":"sceneSwitched","params":{"resource":"ScenesService","args":[]}}"""));
	}

	[Test]
	public void Unsubscribe_TargetsTheSubscriptionResource()
	{
		var json = StreamlabsRpcFrames.Unsubscribe(5, "ScenesService.sceneSwitched");

		Assert.That(json,
			Is.EqualTo(
				"""{"jsonrpc":"2.0","id":5,"method":"unsubscribe","params":{"resource":"ScenesService.sceneSwitched","args":[]}}"""));
	}

	[Test]
	public void Request_WritesADoubleWithoutTheCurrentCulture()
	{
		var json = StreamlabsRpcFrames.Request(1, "AudioSource[\"a\"]", "setDeflection", [0.5d]);

		Assert.That(json, Does.Contain("[0.5]"));
	}

	[Test]
	public void ResourceIds_EscapeAQuoteInTheId()
	{
		Assert.That(StreamlabsRpcFrames.SceneResource("a\"b"), Is.EqualTo("""Scene["a\"b"]"""));
	}

	[Test]
	public void SceneItemResource_CarriesAllThreeIds()
	{
		Assert.That(StreamlabsRpcFrames.SceneItemResource("s", "n", "src"),
			Is.EqualTo("""SceneItem["s","n","src"]"""));
	}

	[Test]
	public void TryParse_ReadsAPlainResult()
	{
		var parsed = StreamlabsRpcFrames.TryParse("""{"jsonrpc":"2.0","id":1,"result":true}""", out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Kind, Is.EqualTo(StreamlabsFrameKind.Result));
			Assert.That(frame.Id, Is.EqualTo(1));
			Assert.That(frame.Data.ValueKind, Is.EqualTo(JsonValueKind.True));
		});
	}

	[Test]
	public void TryParse_ReadsAnError()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"Method not found"}}""",
			out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Kind, Is.EqualTo(StreamlabsFrameKind.Error));
			Assert.That(frame.ErrorCode, Is.EqualTo(-32601));
			Assert.That(frame.ErrorMessage, Is.EqualTo("Method not found"));
		});
	}

	[Test]
	public void TryParse_ReadsAStreamSubscription()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","id":3,"result":{"_type":"SUBSCRIPTION","resourceId":"ScenesService.sceneSwitched","emitter":"STREAM"}}""",
			out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Kind, Is.EqualTo(StreamlabsFrameKind.Subscription));
			Assert.That(frame.ResourceId, Is.EqualTo("ScenesService.sceneSwitched"));
			Assert.That(frame.IsPromise, Is.False);
		});
	}

	[Test]
	public void TryParse_ReadsAPromiseHandle()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","id":5,"result":{"_type":"SUBSCRIPTION","resourceId":"5c3cf84f797a","emitter":"PROMISE"}}""",
			out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Kind, Is.EqualTo(StreamlabsFrameKind.Subscription));
			Assert.That(frame.IsPromise, Is.True);
			Assert.That(frame.ResourceId, Is.EqualTo("5c3cf84f797a"));
		});
	}

	[Test]
	public void TryParse_ReadsAnIdlessStreamEvent()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"STREAM","resourceId":"ScenesService.sceneSwitched","data":{"id":"s1","name":"Gameplay"}}}""",
			out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Kind, Is.EqualTo(StreamlabsFrameKind.Event));
			Assert.That(frame.Id, Is.Null);
			Assert.That(frame.IsPromise, Is.False);
			Assert.That(frame.Data.GetProperty("name").GetString(), Is.EqualTo("Gameplay"));
		});
	}

	[Test]
	public void TryParse_ReadsARejectedPromiseEvent()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"PROMISE","resourceId":"abc","data":{},"isRejected":true}}""",
			out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Kind, Is.EqualTo(StreamlabsFrameKind.Event));
			Assert.That(frame.IsPromise, Is.True);
			Assert.That(frame.IsRejected, Is.True);
		});
	}

	[Test]
	public void TryParse_ReadsAResolvedPromiseEvent()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"PROMISE","resourceId":"abc","data":{},"isRejected":false}}""",
			out var frame);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.IsRejected, Is.False);
		});
	}

	[Test]
	public void TryParse_DataSurvivesTheParsingDocument()
	{
		var parsed = StreamlabsRpcFrames.TryParse(
			"""{"jsonrpc":"2.0","result":{"_type":"EVENT","emitter":"STREAM","resourceId":"r","data":{"name":"kept"}}}""",
			out var frame);

		GC.Collect();

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(frame.Data.GetProperty("name").GetString(), Is.EqualTo("kept"));
		});
	}

	[TestCase("not json")]
	[TestCase("[1,2,3]")]
	[TestCase("""{"jsonrpc":"2.0","id":1}""")]
	[TestCase("")]
	public void TryParse_RejectsWhatItCannotUse(string text)
	{
		Assert.That(StreamlabsRpcFrames.TryParse(text, out _), Is.False);
	}
}
