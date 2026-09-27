using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting.Capabilities.Ui;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.IconPacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeck.Plugin.Hosting.IconPacks;

internal enum BundledIconPackSyncMode
{
	Off,

	Sync,

	Watch
}

internal sealed class BundledIconPackSync : IHostedService, IDisposable
{
	public const string ConfigurationKey = PluginHostOptions.SectionName + ":BundledIconPacks";

	public const string RootConfigurationKey = PluginHostOptions.SectionName + ":BundledIconPacksRoot";

	private const string PackMediaType = "application/zip";

	private static readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(300);

	private static readonly StringComparer _pathComparer =
		OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

	private readonly string _contentRoot;
	private readonly IHostInvoker _invoker;
	private readonly IPluginAssetUploader _uploader;
	private readonly PluginConnectionState _connectionState;
	private readonly IUiSessionReloader _reloader;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

	private readonly CancellationTokenSource _stopping = new();
	private readonly Lock _watchGate = new();
	private readonly Dictionary<string, FileSystemWatcher> _watchers = new(_pathComparer);
	private HashSet<string> _watchedFiles = new(_pathComparer);
	private ITimer? _debounceTimer;
	private Task _loop = Task.CompletedTask;
	private bool _unsupported;

	public BundledIconPackSync(IConfiguration configuration,
		IHostEnvironment environment,
		IHostInvoker invoker,
		IPluginAssetUploader uploader,
		PluginConnectionState connectionState,
		IUiSessionReloader reloader,
		TimeProvider timeProvider,
		ILogger logger)
	{
		Mode = ParseMode(configuration[ConfigurationKey]);
		_contentRoot = Path.GetFullPath(configuration[RootConfigurationKey] is { Length: > 0 } root
			? root
			: environment.ContentRootPath);
		_invoker = invoker;
		_uploader = uploader;
		_connectionState = connectionState;
		_reloader = reloader;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<BundledIconPackSync>();
	}

	public BundledIconPackSyncMode Mode { get; }

	private string ManifestPath => Path.Combine(_contentRoot, PluginManifestFileReader.FileName);

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (Mode == BundledIconPackSyncMode.Off)
		{
			return Task.CompletedTask;
		}

		if (Mode == BundledIconPackSyncMode.Watch)
		{
			_debounceTimer = _timeProvider.CreateTimer(_ => Request(), null, Timeout.InfiniteTimeSpan,
				Timeout.InfiniteTimeSpan);
			Watch([ManifestPath]);
		}

		_connectionState.Connected += OnConnected;
		_loop = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);

		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_connectionState.Connected -= OnConnected;
		_requests.Writer.TryComplete();
		await _stopping.CancelAsync().ConfigureAwait(false);

		try
		{
			await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}

		DisposeWatchers();
	}

	public void Dispose()
	{
		_connectionState.Connected -= OnConnected;
		_stopping.Cancel();
		DisposeWatchers();
		_stopping.Dispose();
	}

	private static BundledIconPackSyncMode ParseMode(string? value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"sync" => BundledIconPackSyncMode.Sync,
			"watch" => BundledIconPackSyncMode.Watch,
			_ => BundledIconPackSyncMode.Off
		};

	private void OnConnected(object? sender, PluginConnectedEventArgs e) => Request();

	private void Request() => _requests.Writer.TryWrite(true);

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var _ in _requests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				if (_unsupported || _connectionState.Status != PluginConnectionStatus.Connected)
				{
					continue;
				}

				await SyncOnceAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private async Task SyncOnceAsync(CancellationToken cancellationToken)
	{
		var entries = ReadDeclaredEntries();

		if (Mode == BundledIconPackSyncMode.Watch)
		{
			Watch([ManifestPath, .. entries?.Select(entry => entry.FullPath) ?? []]);
		}

		if (entries is null || LoadPacks(entries) is not { } packs)
		{
			return;
		}

		var arguments = new IconPackSyncArguments
		{
			Packs =
			[
				.. packs.Select(pack => new BundledIconPackDeclarationDto
				{
					Key = pack.Key, ContentHash = pack.ContentHash, ByteLength = pack.Bytes.Length
				})
			]
		};

		try
		{
			var result = await SyncBundledAsync(arguments, cancellationToken).ConfigureAwait(false);

			if (result.UploadRequired.Count > 0)
			{
				foreach (var hash in result.UploadRequired.Distinct(StringComparer.Ordinal))
				{
					if (packs.FirstOrDefault(pack => string.Equals(pack.ContentHash, hash, StringComparison.Ordinal))
						is { } pack)
					{
						await _uploader.UploadAsync(AssetKinds.IconPack, PackMediaType, pack.Bytes, cancellationToken)
							.ConfigureAwait(false);
					}
				}

				result = await SyncBundledAsync(arguments, cancellationToken).ConfigureAwait(false);

				if (result.UploadRequired.Count > 0)
				{
					_logger.BundledIconPackSyncSkipped("the host still asks for archives after they were uploaded.");
					return;
				}
			}

			_logger.BundledIconPacksSynced(packs.Count, result.Changed);

			if (result.Changed)
			{
				_reloader.ReloadOpenSessions();
			}
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
			_unsupported = true;
			if (packs.Count > 0)
			{
				_logger.BundledIconPacksUnsupported();
			}
		}
		catch (HostInvocationException exception)
		{
			_logger.BundledIconPackSyncRefused(exception.Code, exception.Message);
		}
		catch (AssetUploadException exception)
		{
			_logger.BundledIconPackSyncFailed(exception);
		}
		catch (JsonException exception)
		{
			_logger.BundledIconPackSyncFailed(exception);
		}
	}

	private async Task<IconPackSyncResult> SyncBundledAsync(IconPackSyncArguments arguments,
		CancellationToken cancellationToken)
	{
		var result = await _invoker.InvokeAsync(HostApis.IconPacks,
				HostOperations.IconPacks.SyncBundled,
				arguments,
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Deserialize<IconPackSyncResult>(PluginProtocolJson.Options) ?? new IconPackSyncResult();
	}

	private List<DeclaredEntry>? ReadDeclaredEntries()
	{
		var problems = new List<string>();
		var manifest = PluginManifestFileReader.Read(_contentRoot, problems);
		if (manifest is null)
		{
			_logger.BundledIconPackSyncSkipped(string.Join(" ", problems));
			return null;
		}

		var entries = new List<DeclaredEntry>();
		foreach (var entry in manifest.BundledIconPacks ?? [])
		{
			if (string.IsNullOrWhiteSpace(entry?.Key) || entry.Path is not { } path ||
				!PluginManifestFileReader.IsSafeRelativeIconPath(path))
			{
				_logger.BundledIconPackSyncSkipped(
					$"the manifest declares a bundled icon pack without a key or with an unsafe path '{entry?.Path}'.");
				return null;
			}

			entries.Add(new DeclaredEntry(entry.Key, path,
				Path.GetFullPath(Path.Combine(_contentRoot, path.Replace('/', Path.DirectorySeparatorChar)))));
		}

		return entries;
	}

	// A pack that cannot be read is never left out of the set: the host would delete what it holds
	// under that key. The whole sync waits for the next change instead.
	private List<DeclaredPack>? LoadPacks(List<DeclaredEntry> entries)
	{
		var packs = new List<DeclaredPack>(entries.Count);
		foreach (var entry in entries)
		{
			try
			{
				var file = new FileInfo(entry.FullPath);
				if (!file.Exists)
				{
					_logger.BundledIconPackSyncSkipped($"the bundled icon pack '{entry.Key}' at '{entry.Path}' does not exist.");
					return null;
				}

				if (file.Length > ProtocolLimits.MaxAssetBytes)
				{
					_logger.BundledIconPackSyncSkipped(
						$"the bundled icon pack '{entry.Key}' at '{entry.Path}' is {file.Length} bytes, over the " +
						$"{ProtocolLimits.MaxAssetBytes} byte limit of a development sync. Install the built plugin instead.");
					return null;
				}

				var bytes = File.ReadAllBytes(entry.FullPath);
				packs.Add(new DeclaredPack(entry.Key, bytes, AssetContentHash.Compute(bytes)));
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				_logger.BundledIconPackSyncSkipped(
					$"the bundled icon pack '{entry.Key}' at '{entry.Path}' could not be read: {exception.Message}");
				return null;
			}
		}

		return packs;
	}

	private void Watch(IReadOnlyCollection<string> files)
	{
		lock (_watchGate)
		{
			if (_stopping.IsCancellationRequested)
			{
				return;
			}

			_watchedFiles = new HashSet<string>(files, _pathComparer);

			var directories = new HashSet<string>(files.Select(file => Path.GetDirectoryName(file)!), _pathComparer);

			foreach (var stale in _watchers.Keys.Where(directory => !directories.Contains(directory)).ToList())
			{
				_watchers.Remove(stale, out var watcher);
				watcher!.Dispose();
			}

			foreach (var directory in directories.Where(directory => !_watchers.ContainsKey(directory)))
			{
				if (!Directory.Exists(directory))
				{
					continue;
				}

				var watcher = new FileSystemWatcher(directory)
				{
					IncludeSubdirectories = false,
					NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size |
						NotifyFilters.CreationTime
				};
				watcher.Changed += OnFileEvent;
				watcher.Created += OnFileEvent;
				watcher.Deleted += OnFileEvent;
				watcher.Renamed += OnFileEvent;
				watcher.EnableRaisingEvents = true;
				_watchers[directory] = watcher;
			}
		}
	}

	private void OnFileEvent(object sender, FileSystemEventArgs e)
	{
		var directory = ((FileSystemWatcher)sender).Path;
		var relevant = IsWatched(Path.Combine(directory, e.Name ?? string.Empty)) ||
			(e is RenamedEventArgs renamed && IsWatched(Path.Combine(directory, renamed.OldName ?? string.Empty)));

		if (relevant)
		{
			_debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
		}
	}

	private bool IsWatched(string path)
	{
		lock (_watchGate)
		{
			return _watchedFiles.Contains(path);
		}
	}

	private void DisposeWatchers()
	{
		lock (_watchGate)
		{
			foreach (var watcher in _watchers.Values)
			{
				watcher.Dispose();
			}

			_watchers.Clear();
			_debounceTimer?.Dispose();
			_debounceTimer = null;
		}
	}

	private sealed record DeclaredEntry(string Key, string Path, string FullPath);

	private sealed record DeclaredPack(string Key, byte[] Bytes, string ContentHash);
}
