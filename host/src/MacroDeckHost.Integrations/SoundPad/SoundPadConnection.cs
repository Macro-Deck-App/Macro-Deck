using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.SoundPad;

internal sealed class SoundPadUnavailableException(string message, Exception? innerException = null)
	: Exception(message, innerException);

internal sealed class SoundPadConnection : IDisposable
{
	private static readonly TimeSpan _defaultCommandTimeout = TimeSpan.FromSeconds(3);

	public static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

	private static readonly TimeSpan _defaultRetryInterval = TimeSpan.FromSeconds(5);

	private static readonly ILogger _logger =
		IntegrationLog.For<SoundPadConnection>(SoundPadIntegration.IntegrationId);

	private readonly Func<ISoundPadClient> _clientFactory;
	private readonly TimeSpan _retryInterval;
	private readonly TimeSpan _commandTimeout;
	private readonly Lock _gate = new();

	private ISoundPadClient? _client;
	private CancellationTokenSource? _loop;

	public SoundPadConnection(Func<ISoundPadClient> clientFactory, TimeSpan? retryInterval = null,
		TimeSpan? commandTimeout = null)
	{
		_clientFactory = clientFactory;
		_retryInterval = retryInterval ?? _defaultRetryInterval;
		_commandTimeout = commandTimeout ?? _defaultCommandTimeout;
	}

	public bool IsConnected => _client is { IsConnected: true };

	public void Start()
	{
		CancellationToken token;
		lock (_gate)
		{
			if (_loop is not null)
			{
				return;
			}

			_client = _clientFactory();
			_loop = new CancellationTokenSource();
			token = _loop.Token;
		}

		_ = Task.Run(() => KeepConnectedAsync(token), CancellationToken.None);
	}

	public void Dispose()
	{
		ISoundPadClient? client;
		CancellationTokenSource? loop;
		lock (_gate)
		{
			client = _client;
			loop = _loop;
			_client = null;
			_loop = null;
		}

		loop?.Cancel();
		loop?.Dispose();
		client?.Dispose();
	}

	public Task RunAsync(Func<ISoundPadClient, Task> call, CancellationToken cancellationToken,
		bool connectOnDemand = false, TimeSpan? timeout = null)
		=> RunAsync(async client =>
			{
				await call(client);
				return true;
			},
			cancellationToken,
			connectOnDemand,
			timeout);

	public async Task<T> RunAsync<T>(Func<ISoundPadClient, Task<T>> call, CancellationToken cancellationToken,
		bool connectOnDemand = false, TimeSpan? timeout = null)
	{
		var client = _client ?? throw new SoundPadUnavailableException("The SoundPad connection is stopped.");

		if (!client.IsConnected && (!connectOnDemand || !await TryConnectAsync(client, cancellationToken)))
		{
			throw new SoundPadUnavailableException("SoundPad is not running.");
		}

		try
		{
			return await Bound(call(client), timeout ?? _commandTimeout, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (SoundPadCommandException)
		{
			throw;
		}
		catch (Exception ex)
		{
			// The library offers no per-call cancellation: dropping the pipe is the only way to end a stuck
			// call, and the keep-alive loop opens a new one.
			client.Disconnect();
			throw new SoundPadUnavailableException("The SoundPad connection was lost.", ex);
		}
	}

	private async Task KeepConnectedAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			if (_client is { IsConnected: false } client)
			{
				await TryConnectAsync(client, cancellationToken);
			}

			try
			{
				await Task.Delay(_retryInterval, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task<bool> TryConnectAsync(ISoundPadClient client, CancellationToken cancellationToken)
	{
		try
		{
			await Bound(Connect(client), _commandTimeout, cancellationToken);
			_logger.Information("Connected to SoundPad");
			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (Exception ex)
		{
			_logger.Verbose(ex, "SoundPad is not reachable yet");
			client.Disconnect();
			return false;
		}
	}

	private static async Task<bool> Connect(ISoundPadClient client)
	{
		await client.ConnectAsync();
		return true;
	}

	private static async Task<T> Bound<T>(Task<T> task, TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			return await task.WaitAsync(timeout, cancellationToken);
		}
		catch when (!task.IsCompleted)
		{
			_ = task.ContinueWith(abandoned => _ = abandoned.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
			throw;
		}
	}
}
