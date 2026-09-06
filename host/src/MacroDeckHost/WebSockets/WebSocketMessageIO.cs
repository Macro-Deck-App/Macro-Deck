using System.Net.WebSockets;

namespace MacroDeckHost.WebSockets;

/// <summary>Protocol-neutral framing and bounded-send primitives shared by host WebSocket protocols.</summary>
internal static class WebSocketMessageIO
{
	public static async Task<byte[]?> ReceiveAsync(WebSocket socket,
		int maxMessageBytes,
		bool requireText,
		bool rejectOversized,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var accumulated = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			if (requireText && result.MessageType != WebSocketMessageType.Text)
			{
				throw new InvalidDataException("A text WebSocket message was required.");
			}

			if (accumulated.Length + result.Count > maxMessageBytes)
			{
				if (rejectOversized)
				{
					throw new InvalidDataException("The WebSocket message exceeded the size limit.");
				}
			}
			else
			{
				await accumulated.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
			}

			if (result.EndOfMessage)
			{
				return accumulated.ToArray();
			}
		}
	}

	public static async Task SendTextAsync(WebSocket socket,
		ReadOnlyMemory<byte> payload,
		TimeSpan timeout,
		TimeProvider timeProvider,
		CancellationToken cancellationToken)
	{
		var sendTask = socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).AsTask();
		var timeoutTask = Task.Delay(timeout, timeProvider, CancellationToken.None);
		if (ReferenceEquals(await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(false), timeoutTask))
		{
			ObserveAbandoned(sendTask);
			socket.Abort();
			throw new OperationCanceledException("The WebSocket send did not complete within the timeout.");
		}

		await sendTask.ConfigureAwait(false);
	}

	private static void ObserveAbandoned(Task task)
		=> _ = task.ContinueWith(static completed => _ = completed.Exception,
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
}
