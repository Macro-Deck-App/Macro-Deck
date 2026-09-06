using System.IO.Pipes;
using System.Net.Sockets;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed class DiscordIpcTransport : IDiscordIpcTransport
{
	private static readonly ILogger _logger = IntegrationLog.For<DiscordIpcTransport>(DiscordIntegration.IntegrationId);
	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(2);

	private readonly SemaphoreSlim _writeLock = new(1, 1);

	private Stream? _stream;
	private bool _disposed;

	public bool IsConnected => _stream is not null && !_disposed;

	public string? Endpoint { get; private set; }

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_stream is not null)
		{
			throw new InvalidOperationException("The transport is already connected.");
		}

		var accessDenied = false;
		var attempted = 0;

		foreach (var candidate in Candidates())
		{
			cancellationToken.ThrowIfCancellationRequested();
			attempted++;

			try
			{
				_stream = await OpenAsync(candidate, cancellationToken).ConfigureAwait(false);
				Endpoint = candidate.Description;
				_logger.Debug("Connected to Discord IPC endpoint {Endpoint}", Endpoint);
				return;
			}
			catch (UnauthorizedAccessException ex)
			{
				accessDenied = true;
				_logger.Debug(ex, "Discord IPC endpoint {Endpoint} refused access", candidate.Description);
			}
			catch (Exception ex) when (ex is IOException or SocketException or TimeoutException)
			{
				_logger.Debug(ex, "Discord IPC endpoint {Endpoint} not usable", candidate.Description);
			}
		}

		throw new DiscordIpcUnavailableException(attempted == 0
				? "No Discord IPC endpoint exists. Discord does not appear to be running."
				: "No Discord IPC endpoint accepted the connection.",
			accessDenied);
	}

	public async Task WriteFrameAsync(
		DiscordRpcOpcode opcode,
		ReadOnlyMemory<byte> payload,
		CancellationToken cancellationToken)
	{
		var stream = _stream ?? throw new DiscordIpcProtocolException("The transport is not connected.");

		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await DiscordIpcFraming.WriteFrameAsync(stream, opcode, payload, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public Task<DiscordIpcFrame?> ReadFrameAsync(CancellationToken cancellationToken)
	{
		var stream = _stream ?? throw new DiscordIpcProtocolException("The transport is not connected.");
		return DiscordIpcFraming.ReadFrameAsync(stream, cancellationToken);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			_stream?.Dispose();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while closing the Discord IPC stream");
		}

		_stream = null;
		_writeLock.Dispose();
	}

	private static IEnumerable<IpcCandidate> Candidates()
	{
		if (OperatingSystem.IsWindows())
		{
			foreach (var pipe in DiscordIpcEndpointLocator.WindowsPipeNames())
			{
				if (File.Exists($@"\\.\pipe\{pipe}"))
				{
					yield return new IpcCandidate(pipe, $@"\\.\pipe\{pipe}");
				}
			}

			yield break;
		}

		foreach (var path in DiscordIpcEndpointLocator.UnixSocketPaths())
		{
			if (File.Exists(path))
			{
				yield return new IpcCandidate(path, path);
			}
		}
	}

	private static async Task<Stream> OpenAsync(IpcCandidate candidate, CancellationToken cancellationToken)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(_connectTimeout);

		if (OperatingSystem.IsWindows())
		{
			var pipe = new NamedPipeClientStream(".", candidate.Target, PipeDirection.InOut, PipeOptions.Asynchronous);
			try
			{
				await pipe.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
				return pipe;
			}
			catch
			{
				await pipe.DisposeAsync().ConfigureAwait(false);
				throw;
			}
		}

		var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(candidate.Target), timeoutCts.Token)
				.ConfigureAwait(false);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private readonly record struct IpcCandidate(string Target, string Description);
}
