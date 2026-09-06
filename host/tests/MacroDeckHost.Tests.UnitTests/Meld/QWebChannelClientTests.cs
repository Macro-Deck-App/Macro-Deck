using System.Diagnostics;
using System.Text.Json;
using MacroDeckHost.Integrations.Meld.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class QWebChannelClientTests
{
	private const string MeldClassInfoData =
		"""
		{"meld":{
		  "methods":[["showScene",0],["showScene(QString)",0],["setStagedScene",1],["toggleMute",2]],
		  "signals":[["gainUpdated",0]],
		  "properties":[[0,"version",["versionChanged",1],2],[1,"session",["sessionChanged",2],{"items":{}}]],
		  "enums":{}
		}}
		""";

	private static readonly Uri _uri = new("ws://127.0.0.1:13376/");

	private WebSocketPair _pair = null!;
	private QWebChannelClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_pair = await WebSocketPair.CreateAsync();
		_client = new QWebChannelClient((_, _) => Task.FromResult(_pair.Client),
			initTimeout: TimeSpan.FromMilliseconds(500),
			invokeTimeout: TimeSpan.FromMilliseconds(500));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_pair.Dispose();
	}

	[Test]
	public async Task ConnectAsync_sends_init_with_an_id_then_idle_after_the_response()
	{
		var connecting = _client.ConnectAsync(_uri, CancellationToken.None);

		var initRaw = await _pair.ReceiveAsync();
		using var initRequest = JsonDocument.Parse(initRaw);
		Assert.Multiple(() =>
		{
			Assert.That(initRequest.RootElement.GetProperty("type").GetInt32(),
				Is.EqualTo(QWebChannelMessageTypes.Init));
			Assert.That(initRequest.RootElement.TryGetProperty("id", out _), Is.True);
		});

		var id = initRequest.RootElement.GetProperty("id").GetInt32();
		await _pair.SendAsync($$"""{"type":10,"id":{{id}},"data":{{MeldClassInfoData}}}""");

		var idleRaw = await _pair.ReceiveAsync();
		AssertIsIdle(idleRaw);

		var objects = await connecting;
		Assert.That(objects.ContainsKey("meld"), Is.True);
	}

	[Test]
	public async Task Signature_duplicate_methods_are_skipped_and_properties_resolve_names_and_initial_values()
	{
		var objects = await ConnectHandshakeAsync();
		var meld = objects["meld"];

		Assert.Multiple(() =>
		{
			Assert.That(meld.Methods["showScene"], Is.EqualTo(0));
			Assert.That(meld.Methods, Does.Not.ContainKey("showScene(QString)"));
			Assert.That(meld.Methods["setStagedScene"], Is.EqualTo(1));
			Assert.That(meld.PropertyNames[0], Is.EqualTo("version"));
			Assert.That(meld.PropertyNames[1], Is.EqualTo("session"));
			Assert.That(meld.InitialProperties["version"].GetInt32(), Is.EqualTo(2));
			Assert.That(meld.InitialProperties["session"].GetProperty("items").ValueKind,
				Is.EqualTo(JsonValueKind.Object));
			Assert.That(meld.Signals["gainUpdated"], Is.EqualTo(0));
			Assert.That(meld.SignalNames[0], Is.EqualTo("gainUpdated"));
		});
	}

	[Test]
	public async Task Every_propertyUpdate_including_one_for_an_unknown_object_is_followed_by_idle()
	{
		await ConnectHandshakeAsync();

		await _pair.SendAsync("""{"type":2,"data":[{"object":"meld","signals":{},"properties":{"1":{"items":{}}}}]}""");
		AssertIsIdle(await _pair.ReceiveAsync());

		await _pair.SendAsync("""{"type":2,"data":[{"object":"unknown","signals":{},"properties":{"0":1}}]}""");
		AssertIsIdle(await _pair.ReceiveAsync());
	}

	[Test]
	public async Task A_propertyUpdate_raises_PropertyUpdated_with_the_property_name_resolved()
	{
		await ConnectHandshakeAsync();

		var received =
			new TaskCompletionSource<QWebChannelPropertyUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.PropertyUpdated += (_, update) => received.TrySetResult(update);

		await _pair.SendAsync("""{"type":2,"data":[{"object":"meld","signals":{},"properties":{"0":3}}]}""");
		await _pair.ReceiveAsync(); // the idle credit

		var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Multiple(() =>
		{
			Assert.That(update.Object, Is.EqualTo("meld"));
			Assert.That(update.Properties["version"].GetInt32(), Is.EqualTo(3));
		});
	}

	[Test]
	public async Task InvokeAsync_sends_an_id_and_two_overlapping_invokes_resolve_independently()
	{
		await ConnectHandshakeAsync();

		var first = _client.InvokeAsync("meld", "showScene", ["scene-1"], CancellationToken.None);
		var firstRaw = await _pair.ReceiveAsync();
		using var firstRequest = JsonDocument.Parse(firstRaw);
		var firstId = firstRequest.RootElement.GetProperty("id").GetInt32();
		Assert.That(firstRequest.RootElement.GetProperty("method").GetInt32(), Is.EqualTo(0));

		var second = _client.InvokeAsync("meld", "setStagedScene", ["scene-2"], CancellationToken.None);
		var secondRaw = await _pair.ReceiveAsync();
		using var secondRequest = JsonDocument.Parse(secondRaw);
		var secondId = secondRequest.RootElement.GetProperty("id").GetInt32();

		Assert.That(firstId, Is.Not.EqualTo(secondId));

		await _pair.SendAsync($$"""{"type":10,"id":{{secondId}},"data":"second"}""");
		await _pair.SendAsync($$"""{"type":10,"id":{{firstId}},"data":"first"}""");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await first).GetString(), Is.EqualTo("first"));
			Assert.That((await second).GetString(), Is.EqualTo("second"));
		});
	}

	[Test]
	public async Task InvokeAsync_of_an_unknown_method_throws_without_sending()
	{
		await ConnectHandshakeAsync();

		Assert.ThrowsAsync<QWebChannelException>(async () =>
			await _client.InvokeAsync("meld", "doesNotExist", [], CancellationToken.None));

		var receiving = _pair.ReceiveAsync();
		var completed = await Task.WhenAny(receiving, Task.Delay(TimeSpan.FromMilliseconds(200)));
		Assert.That(completed, Is.Not.SameAs(receiving));
	}

	[Test]
	public async Task ConnectToSignalAsync_sends_the_resolved_index_without_an_id()
	{
		await ConnectHandshakeAsync();

		var task = _client.ConnectToSignalAsync("meld", "gainUpdated", CancellationToken.None);
		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);

		Assert.Multiple(() =>
		{
			Assert.That(request.RootElement.GetProperty("type").GetInt32(),
				Is.EqualTo(QWebChannelMessageTypes.ConnectToSignal));
			Assert.That(request.RootElement.GetProperty("signal").GetInt32(), Is.EqualTo(0));
			Assert.That(request.RootElement.TryGetProperty("id", out _), Is.False);
		});

		await task;
	}

	[Test]
	public async Task A_pushed_signal_is_raised_with_its_name_resolved_and_args_in_order()
	{
		await ConnectHandshakeAsync();

		var received
			= new TaskCompletionSource<QWebChannelSignalMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.SignalReceived += (_, message) => received.TrySetResult(message);

		await _pair.SendAsync("""{"type":1,"object":"meld","signal":0,"args":["track-1",0.75,false]}""");

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(message.Object, Is.EqualTo("meld"));
			Assert.That(message.Signal, Is.EqualTo("gainUpdated"));
			Assert.That(message.Args[0].GetString(), Is.EqualTo("track-1"));
			Assert.That(message.Args[1].GetDouble(), Is.EqualTo(0.75));
			Assert.That(message.Args[2].GetBoolean(), Is.False);
		});
	}

	[Test]
	public async Task An_unknown_signal_index_is_dropped()
	{
		await ConnectHandshakeAsync();

		var received = false;
		_client.SignalReceived += (_, _) => received = true;

		await _pair.SendAsync("""{"type":1,"object":"meld","signal":99,"args":[]}""");

		var pending = _client.InvokeAsync("meld", "showScene", [], CancellationToken.None);
		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);
		await _pair.SendAsync(
			$$"""{"type":10,"id":{{request.RootElement.GetProperty("id").GetInt32()}},"data":null}""");
		await pending;

		Assert.That(received, Is.False);
	}

	[Test]
	public async Task A_malformed_message_is_discarded_without_killing_the_session()
	{
		await ConnectHandshakeAsync();

		await _pair.SendAsync("this is not json");

		var pending = _client.InvokeAsync("meld", "showScene", [], CancellationToken.None);
		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);
		await _pair.SendAsync(
			$$"""{"type":10,"id":{{request.RootElement.GetProperty("id").GetInt32()}},"data":null}""");

		Assert.DoesNotThrowAsync(async () => await pending);
	}

	[Test]
	public async Task An_oversized_frame_ends_the_session()
	{
		await ConnectHandshakeAsync();

		var disconnects = 0;
		_client.Disconnected += (_, _) => Interlocked.Increment(ref disconnects);

		var huge = new string('a', QWebChannelClient.MaxMessageBytes + 1);
		await _pair.SendAsync($$"""{"type":5,"debug":"{{huge}}"}""");

		await WaitForAsync(() => disconnects == 1, "the session to end");
	}

	[Test]
	public async Task A_dropped_connection_faults_pending_invokes_and_reports_it_once()
	{
		await ConnectHandshakeAsync();

		var disconnects = 0;
		_client.Disconnected += (_, _) => Interlocked.Increment(ref disconnects);

		var pending = _client.InvokeAsync("meld", "showScene", [], CancellationToken.None);
		await _pair.ReceiveAsync();
		_pair.Break();

		Assert.ThrowsAsync<QWebChannelException>(async () => await pending);
		await WaitForAsync(() => Volatile.Read(ref disconnects) == 1, "the drop to be reported");

		_client.Dispose();
		Assert.That(Volatile.Read(ref disconnects), Is.EqualTo(1));
	}

	[Test]
	public async Task A_second_ConnectAsync_throws()
	{
		await ConnectHandshakeAsync();

		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await _client.ConnectAsync(_uri, CancellationToken.None));
	}

	[Test]
	public void Double_dispose_is_safe()
	{
		Assert.DoesNotThrow(() =>
		{
			_client.Dispose();
			_client.Dispose();
		});
	}

	private async Task<IReadOnlyDictionary<string, QWebChannelObjectInfo>> ConnectHandshakeAsync()
	{
		var connecting = _client.ConnectAsync(_uri, CancellationToken.None);

		var initRaw = await _pair.ReceiveAsync();
		using var initRequest = JsonDocument.Parse(initRaw);
		var id = initRequest.RootElement.GetProperty("id").GetInt32();

		await _pair.SendAsync($$"""{"type":10,"id":{{id}},"data":{{MeldClassInfoData}}}""");
		await _pair.ReceiveAsync(); // the idle credit after init

		return await connecting;
	}

	private static void AssertIsIdle(string raw)
	{
		using var document = JsonDocument.Parse(raw);
		Assert.That(document.RootElement.GetProperty("type").GetInt32(), Is.EqualTo(QWebChannelMessageTypes.Idle));
	}

	private static async Task WaitForAsync(Func<bool> condition, string because)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, $"Timed out waiting for {because}.");
	}
}
