using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeck.Plugin.Cli.Rendering;

internal sealed partial class CdpConnection : IAsyncDisposable
{
	private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(30);

	private readonly Process _process;
	private readonly ClientWebSocket _socket;
	private readonly CancellationTokenSource _stopping = new();
	private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly Task _reader;
	private int _nextId;

	private CdpConnection(Process process, ClientWebSocket socket)
	{
		_process = process;
		_socket = socket;
		_reader = Task.Run(ReadLoopAsync);
	}

	public static async Task<CdpConnection> LaunchAsync(string browser, string profileDirectory, CancellationToken cancellationToken)
	{
		var start = new ProcessStartInfo(browser)
		{
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false
		};

		foreach (var argument in new[]
			{
				"--headless=new",
				"--hide-scrollbars",
				"--no-first-run",
				"--no-default-browser-check",
				"--disable-extensions",
				"--remote-debugging-port=0",
				$"--user-data-dir={profileDirectory}",
				"about:blank"
			})
		{
			start.ArgumentList.Add(argument);
		}

		if (OperatingSystem.IsLinux() && Environment.UserName == "root")
		{
			start.ArgumentList.Add("--no-sandbox");
		}

		var process = Process.Start(start) ??
			throw new PreviewRenderException("browser-launch-failed", $"The browser '{browser}' did not start.");
		_ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(_startTimeout);
			var endpoint = await ReadEndpointAsync(process, timeout.Token).ConfigureAwait(false);
			_ = process.StandardError.ReadToEndAsync(CancellationToken.None);

			var socket = new ClientWebSocket();
			await socket.ConnectAsync(new Uri(endpoint), timeout.Token).ConfigureAwait(false);

			return new CdpConnection(process, socket);
		}
		catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
		{
			process.Kill(entireProcessTree: true);
			cancellationToken.ThrowIfCancellationRequested();

			throw new PreviewRenderException("browser-launch-failed",
				$"The browser '{browser}' did not open its debugging endpoint within {_startTimeout.TotalSeconds:0} seconds.");
		}
	}

	public async Task<JsonElement> SendAsync(string method, object? parameters, string? sessionId, CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _nextId);
		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		var message = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
		{
			["id"] = id,
			["method"] = method,
			["params"] = parameters ?? new { },
			["sessionId"] = sessionId
		}.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value));

		await _socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

		return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync().ConfigureAwait(false);

		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}

			await _process.WaitForExitAsync().ConfigureAwait(false);
		}
		catch (InvalidOperationException)
		{
		}

		_socket.Dispose();
		await _reader.ConfigureAwait(false);
		_process.Dispose();
		_stopping.Dispose();
	}

	private static async Task<string> ReadEndpointAsync(Process process, CancellationToken cancellationToken)
	{
		while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
		{
			if (Endpoint().Match(line) is { Success: true } match)
			{
				return match.Value;
			}
		}

		throw new PreviewRenderException("browser-launch-failed", "The browser exited before it opened a debugging endpoint.");
	}

	private async Task ReadLoopAsync()
	{
		var buffer = new byte[64 * 1024];

		try
		{
			while (!_stopping.IsCancellationRequested)
			{
				using var message = new MemoryStream();
				ValueWebSocketReceiveResult received;

				do
				{
					received = await _socket.ReceiveAsync(buffer.AsMemory(), _stopping.Token).ConfigureAwait(false);
					message.Write(buffer, 0, received.Count);
				}
				while (!received.EndOfMessage);

				if (received.MessageType == WebSocketMessageType.Close)
				{
					break;
				}

				using var document = JsonDocument.Parse(message.ToArray());

				if (!document.RootElement.TryGetProperty("id", out var id) || !_pending.TryRemove(id.GetInt32(), out var completion))
				{
					continue;
				}

				if (document.RootElement.TryGetProperty("error", out var error))
				{
					completion.TrySetException(new PreviewRenderException("browser-protocol-error", error.GetRawText()));
				}
				else
				{
					completion.TrySetResult(document.RootElement.GetProperty("result").Clone());
				}
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
		{
		}

		foreach (var completion in _pending.Values)
		{
			completion.TrySetException(new PreviewRenderException("browser-closed", "The browser closed the connection."));
		}
	}

	[GeneratedRegex(@"ws://\S+")]
	private static partial Regex Endpoint();
}
