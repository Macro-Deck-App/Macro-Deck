using System.Diagnostics;
using MacroDeckHost.Integrations.Meld;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Meld;

[TestFixture]
internal sealed class MeldVariableCatalogTests
{
	private const string SessionJson =
		"""
		{"items":{
		"scene1":{"current":true,"index":0,"name":"Scene One","staged":false,"type":"scene"},
		"track1":{"name":"Track One","muted":false,"monitoring":false,"type":"track"}
		}}
		""";

	private static readonly Uri _uri = new("ws://127.0.0.1:13377/");

	[Test]
	public async Task A_track_gain_reads_back_as_a_percentage_with_its_own_range()
	{
		var (connection, client) = await ConnectedWithClientAsync();
		using var connectionDisposable = connection;
		client.RaiseSignal(MeldObjects.Object,
			MeldObjects.GainUpdatedSignal,
			FakeQWebChannelClient.Parse("\"track1\""),
			FakeQWebChannelClient.Parse("0.42"),
			FakeQWebChannelClient.Parse("false"));
		await WaitForAsync(() => connection.TryGetGain("track1", out _), "the gain to be cached");

		var catalog = new MeldVariableCatalog(() => connection);

		var reading = await catalog.ReadAsync(await GainIdAsync(catalog, connection));

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.EqualTo(42d).Within(0.001));
			Assert.That(reading.Min, Is.EqualTo(0));
			Assert.That(reading.Max, Is.EqualTo(100));
			Assert.That(reading.Step, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_unknown_track_reads_back_unavailable_rather_than_zero()
	{
		using var connection = await ConnectedAsync();
		var catalog = new MeldVariableCatalog(() => connection);

		var reading = await catalog.ReadAsync("track/does-not-exist/gain");

		Assert.That(reading.Value, Is.Null);
	}

	/// <summary>Only the gain accepts a write; mute and monitoring are toggled through their own actions,
	/// and a write to one has to be refused rather than silently ignored.</summary>
	[Test]
	public async Task Only_the_gain_leaf_accepts_a_write()
	{
		using var connection = await ConnectedAsync();
		var catalog = new MeldVariableCatalog(() => connection);
		var gainId = await GainIdAsync(catalog, connection);

		var muted = await catalog.SetValueAsync(gainId[..^"gain".Length] + "muted", true);

		Assert.That(muted.Status, Is.EqualTo(VariableWriteStatus.NotWritable));
	}

	private static async Task<string> GainIdAsync(MeldVariableCatalog catalog, MeldConnection connection)
	{
		_ = connection;
		var tracks = await catalog.DiscoverAsync(new VariableCatalogQuery());
		var track = tracks.Items.Single();
		var leaves = await catalog.DiscoverAsync(new VariableCatalogQuery { ParentId = track.Id });

		return leaves.Items.Single(leaf => leaf.Id!.EndsWith("/gain", StringComparison.Ordinal)).Id!;
	}

	private static async Task<MeldConnection> ConnectedAsync()
	{
		var (connection, _) = await ConnectedWithClientAsync();
		return connection;
	}

	private static async Task<(MeldConnection Connection, FakeQWebChannelClient Client)> ConnectedWithClientAsync()
	{
		var client = new FakeQWebChannelClient();
		client.Objects["meld"]
			= FakeQWebChannelClient.BuildMeldObjectInfo(sessionJson: SessionJson, supportsSetMuted: true);
		var connection = new MeldConnection(() => client, _uri);
		connection.Start();
		await WaitForAsync(() => connection.IsConnected, "the connection to come up");

		return (connection, client);
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
