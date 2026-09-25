using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.IconPacks;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.IconPacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class BundledIconPackSyncTests
{
	private string _projectDirectory = null!;

	[SetUp]
	public void CreateProject()
	{
		_projectDirectory = Path.Combine(Path.GetTempPath(), "bundled-icon-pack-sync-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(_projectDirectory, "icon-packs"));
	}

	[TearDown]
	public void DeleteProject() => Directory.Delete(_projectDirectory, recursive: true);

	[Test]
	public async Task A_new_session_sends_the_full_declared_set_uploads_what_the_host_asks_for_and_syncs_again()
	{
		var logos = WritePack("logos", [1, 2, 3]);
		var status = WritePack("status", [4, 5]);
		WriteManifest("logos", "status");
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, "sync");

		harness.Connect();
		await WaitForAsync(() => host.Syncs.Count == 2);

		Assert.Multiple(() =>
		{
			Assert.That(host.Operations, Is.All.EqualTo((HostApis.IconPacks, HostOperations.IconPacks.SyncBundled)));
			Assert.That(host.Syncs[0], Is.EqualTo(host.Syncs[1]));
			Assert.That(host.Syncs[0].Select(pack => (pack.Key, pack.ContentHash, pack.ByteLength)),
				Is.EqualTo((List<(string, string, int)>)
				[
					("logos", AssetContentHash.Compute(logos), logos.Length),
					("status", AssetContentHash.Compute(status), status.Length)
				]));
			Assert.That(host.Uploads.Select(upload => (upload.Kind, upload.MimeType)),
				Is.All.EqualTo((AssetKinds.IconPack, "application/zip")));
			Assert.That(host.Uploads.Select(upload => upload.Data), Is.EquivalentTo((List<byte[]>)[logos, status]));
		});
	}

	[Test]
	public async Task Only_the_archives_the_host_does_not_hold_are_uploaded()
	{
		var logos = WritePack("logos", [1, 2, 3]);
		var status = WritePack("status", [4, 5]);
		WriteManifest("logos", "status");
		var host = new FakeIconPackHost();
		host.Hold(logos);
		await using var harness = await StartAsync(host, "sync");

		harness.Connect();
		await WaitForAsync(() => host.Syncs.Count == 2);

		Assert.That(host.Uploads.Select(upload => upload.Data), Is.EqualTo((List<byte[]>)[status]));
	}

	[Test]
	public async Task Every_re_established_session_syncs_again_and_only_a_change_reloads_open_views()
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, "sync");

		harness.Connect();
		await WaitForAsync(() => harness.Reloader.Reloads == 1);
		harness.Connect();
		await WaitForAsync(() => host.Syncs.Count == 3);
		await Task.Delay(100);

		Assert.That(harness.Reloader.Reloads, Is.EqualTo(1));
	}

	[Test]
	public async Task A_missing_pack_file_skips_the_whole_sync_and_names_the_file()
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos", "status");
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, "sync");

		harness.Connect();
		await WaitForAsync(() => harness.Log.Warnings().Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(host.Syncs, Is.Empty);
			Assert.That(harness.Log.Warnings().Single(), Does.Contain("icon-packs/status.macroDeckIconPack"));
		});
	}

	[Test]
	public async Task A_host_without_bundled_icon_packs_is_reported_once_and_not_asked_again()
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var host = new FakeIconPackHost { RefuseWith = ProtocolErrorCodes.CapabilityUnsupported };
		await using var harness = await StartAsync(host, "sync");

		harness.Connect();
		await WaitForAsync(() => harness.Log.Warnings().Count == 1);
		harness.Connect();
		await Task.Delay(200);

		Assert.Multiple(() =>
		{
			Assert.That(host.Invocations, Is.EqualTo(1));
			Assert.That(harness.Log.Warnings(), Has.Count.EqualTo(1));
		});
	}

	[TestCase(ProtocolErrorCodes.IconPackSyncNotAllowed)]
	[TestCase(ProtocolErrorCodes.IconPackInvalid)]
	public async Task A_refused_sync_is_logged_with_the_hosts_message_and_tried_again_next_session(string code)
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var host = new FakeIconPackHost { RefuseWith = code };
		await using var harness = await StartAsync(host, "sync");

		harness.Connect();
		await WaitForAsync(() => harness.Log.Warnings().Count == 1);
		harness.Connect();
		await WaitForAsync(() => host.Invocations == 2);

		Assert.That(harness.Log.Warnings()[0], Does.Contain(code).And.Contain("refused by the fake host"));
	}

	[Test]
	public async Task Without_the_configuration_key_nothing_is_synced()
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, mode: null);

		harness.Connect();
		await Task.Delay(200);

		Assert.That(host.Invocations, Is.Zero);
	}

	[Test]
	public async Task Watching_resyncs_a_replaced_pack_and_reloads_open_views()
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, "watch");
		harness.Connect();
		await WaitForAsync(() => harness.Reloader.Reloads == 1);

		var replaced = WritePack("logos", [9, 9, 9, 9]);
		await WaitForAsync(() => harness.Reloader.Reloads == 2, TimeSpan.FromSeconds(15));

		Assert.That(host.Syncs[^1].Single().ContentHash, Is.EqualTo(AssetContentHash.Compute(replaced)));
	}

	[Test]
	public async Task Watching_syncs_a_pack_added_to_the_manifest_and_then_watches_it_too()
	{
		WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, "watch");
		harness.Connect();
		await WaitForAsync(() => harness.Reloader.Reloads == 1);

		WritePack("status", [4, 5]);
		WriteManifest("logos", "status");
		await WaitForAsync(() => host.Syncs is [.., { Count: 2 }], TimeSpan.FromSeconds(15));
		var replaced = WritePack("status", [6, 6]);
		await WaitForAsync(() => host.Syncs[^1].Any(pack => pack.ContentHash == AssetContentHash.Compute(replaced)),
			TimeSpan.FromSeconds(15));

		Assert.That(host.Syncs[^1].Select(pack => pack.Key), Is.EqualTo((List<string>)["logos", "status"]));
	}

	private byte[] WritePack(string key, byte[] content)
	{
		File.WriteAllBytes(Path.Combine(_projectDirectory, "icon-packs", key + ".macroDeckIconPack"), content);
		return content;
	}

	private void WriteManifest(params string[] keys)
	{
		var manifest = new
		{
			manifestVersion = 1,
			id = "com.example.icons",
			name = "Icons",
			version = "1.0.0",
			bundledIconPacks = keys.Select(key => new { key, path = $"icon-packs/{key}.macroDeckIconPack" }).ToArray(),
		};

		File.WriteAllText(Path.Combine(_projectDirectory, "manifest.json"), JsonSerializer.Serialize(manifest));
	}

	[Test]
	public async Task A_plugin_running_from_its_build_output_syncs_the_packs_of_its_project_directory()
	{
		var logos = WritePack("logos", [1, 2, 3]);
		WriteManifest("logos");
		var buildOutput = Path.Combine(_projectDirectory, "bin");
		Directory.CreateDirectory(buildOutput);
		var host = new FakeIconPackHost();
		await using var harness = await StartAsync(host, "sync", contentRoot: buildOutput);

		harness.Connect();
		await WaitForAsync(() => host.Syncs.Count == 2);

		Assert.That(host.Uploads.Select(upload => upload.Data), Is.EquivalentTo((List<byte[]>)[logos]));
	}

	private async Task<Harness> StartAsync(FakeIconPackHost host, string? mode, string? contentRoot = null)
	{
		var settings = new Dictionary<string, string?>();
		if (mode is not null)
		{
			settings[BundledIconPackSync.ConfigurationKey] = mode;
		}

		if (contentRoot is not null)
		{
			settings[BundledIconPackSync.RootConfigurationKey] = _projectDirectory;
		}

		var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
		var environment = new HostingEnvironment { ContentRootPath = contentRoot ?? _projectDirectory };
		var connectionState = new PluginConnectionState();
		var reloader = new CountingReloader();
		var log = new CollectingSink();
		var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(log).CreateLogger();

		var sync = new BundledIconPackSync(configuration, environment, host, host, connectionState, reloader,
			TimeProvider.System, logger);
		await sync.StartAsync(CancellationToken.None);

		return new Harness(sync, connectionState, reloader, log);
	}

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(10);
		}

		Assert.Fail("The condition was never met.");
	}

	private sealed record Harness(
		BundledIconPackSync Sync,
		PluginConnectionState ConnectionState,
		CountingReloader Reloader,
		CollectingSink Log) : IAsyncDisposable
	{
		public void Connect()
		{
			ConnectionState.Status = PluginConnectionStatus.Connected;
			ConnectionState.RaiseConnected(resumed: false);
		}

		public async ValueTask DisposeAsync()
		{
			await Sync.StopAsync(CancellationToken.None);
			Sync.Dispose();
		}
	}

	private sealed class CountingReloader : IUiSessionReloader
	{
		private int _reloads;

		public int Reloads => Volatile.Read(ref _reloads);

		public void ReloadOpenSessions() => Interlocked.Increment(ref _reloads);
	}

	private sealed class CollectingSink : ILogEventSink
	{
		private readonly Lock _gate = new();
		private readonly List<LogEvent> _events = [];

		public void Emit(LogEvent logEvent)
		{
			lock (_gate)
			{
				_events.Add(logEvent);
			}
		}

		public IReadOnlyList<string> Warnings()
		{
			lock (_gate)
			{
				return
				[
					.. _events.Where(logEvent => logEvent.Level == LogEventLevel.Warning)
						.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture))
				];
			}
		}
	}

	private sealed class FakeIconPackHost : IHostInvoker, IPluginAssetUploader
	{
		private readonly Lock _gate = new();
		private readonly HashSet<string> _held = new(StringComparer.Ordinal);
		private readonly List<IReadOnlyList<BundledIconPackDeclarationDto>> _syncs = [];
		private readonly List<(string Kind, string MimeType, byte[] Data)> _uploads = [];
		private readonly List<(string Api, string Operation)> _operations = [];
		private Dictionary<string, string> _applied = new(StringComparer.Ordinal);
		private int _invocations;

		public string? RefuseWith { get; init; }

		public int Invocations => Volatile.Read(ref _invocations);

		public IReadOnlyList<IReadOnlyList<BundledIconPackDeclarationDto>> Syncs
		{
			get
			{
				lock (_gate)
				{
					return [.. _syncs];
				}
			}
		}

		public IReadOnlyList<(string Api, string Operation)> Operations
		{
			get
			{
				lock (_gate)
				{
					return [.. _operations];
				}
			}
		}

		public IReadOnlyList<(string Kind, string MimeType, byte[] Data)> Uploads
		{
			get
			{
				lock (_gate)
				{
					return [.. _uploads];
				}
			}
		}

		public void Hold(byte[] archive)
		{
			lock (_gate)
			{
				_held.Add(AssetContentHash.Compute(archive));
			}
		}

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _invocations);
			lock (_gate)
			{
				_operations.Add((api, operation));
			}

			if (RefuseWith is { } code)
			{
				throw HostInvocationException.CreateNonRetryable(code, "refused by the fake host");
			}

			var packs = ((IconPackSyncArguments)arguments!).Packs;

			lock (_gate)
			{
				_syncs.Add(packs);

				var missing = packs.Select(pack => pack.ContentHash).Where(hash => !_held.Contains(hash)).ToList();
				if (missing.Count > 0)
				{
					return Result(new IconPackSyncResult { UploadRequired = missing });
				}

				var declared = packs.ToDictionary(pack => pack.Key, pack => pack.ContentHash, StringComparer.Ordinal);
				var changed = declared.Count != _applied.Count ||
					declared.Any(pair => !_applied.TryGetValue(pair.Key, out var hash) || hash != pair.Value);
				_applied = declared;

				return Result(new IconPackSyncResult { Changed = changed });
			}
		}

		public Task<string> UploadAsync(string kind, string mimeType, byte[] data, CancellationToken cancellationToken)
		{
			var hash = AssetContentHash.Compute(data);
			lock (_gate)
			{
				_uploads.Add((kind, mimeType, data));
				_held.Add(hash);
			}

			return Task.FromResult(hash);
		}

		public bool TryComplete(ProtocolEnvelope result) => false;

		private static Task<JsonElement?> Result(IconPackSyncResult result)
			=> Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(result, PluginProtocolJson.Options));
	}
}
