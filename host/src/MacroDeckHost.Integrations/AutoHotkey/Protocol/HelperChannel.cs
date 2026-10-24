using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Serilog;

namespace MacroDeckHost.Integrations.AutoHotkey.Protocol;

internal sealed record HelperAnswer(bool Ok, string Text);

internal interface IHelperHandler
{
	void OnReady();

	Task SetVariableAsync(string name, string value, CancellationToken cancellationToken);

	Task<HelperAnswer> GetVariableAsync(string name, CancellationToken cancellationToken);

	void Notify(string title, string message, string level);

	void SetResult(string text);

	void Log(string text);

	void ReportError(HelperMessage error);

	string RejectedRequestText();

	string NoAnswerText();
}

internal sealed class HelperChannel : IAsyncDisposable
{
	private const int QueueCapacity = 256;

	private static readonly TimeSpan _answerTimeout = TimeSpan.FromSeconds(5);

	private readonly TextWriter _input;
	private readonly IHelperHandler _handler;
	private readonly ILogger _logger;
	private readonly Channel<HelperMessage> _queue;
	private readonly SemaphoreSlim _writeGate = new(1, 1);
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _consumer;
	private bool _reportedDrop;

	public HelperChannel(TextWriter input, IHelperHandler handler, ILogger logger)
	{
		_input = input;
		_handler = handler;
		_logger = logger;
		_queue = Channel.CreateBounded<HelperMessage>(new BoundedChannelOptions(QueueCapacity)
		{
			SingleReader = true,
			SingleWriter = true,
			FullMode = BoundedChannelFullMode.Wait
		});
		_consumer = Task.Run(ConsumeAsync);
	}

	public void Accept(HelperParseResult result)
	{
		switch (result.Status)
		{
			case HelperParseStatus.Message when result.Message is { Kind: HelperMessageKind.Ready }:
				_handler.OnReady();
				break;
			case HelperParseStatus.Message when result.Message is { } message:
				if (!_queue.Writer.TryWrite(message))
				{
					if (message.Kind == HelperMessageKind.GetVariable)
					{
						_ = AnswerAsync(message.RequestId, new HelperAnswer(false, _handler.RejectedRequestText()));
					}
					else
					{
						ReportDrop("queue full");
					}
				}

				break;
			case HelperParseStatus.RejectedRequest:
				_ = AnswerAsync(result.RequestId, new HelperAnswer(false, _handler.RejectedRequestText()));
				break;
			case HelperParseStatus.Dropped:
				ReportDrop(result.OverRateLimit ? "rate limit" : "too long");
				break;
			case HelperParseStatus.Malformed:
				_logger.Debug("Ignored a malformed AutoHotkey helper message");
				break;
		}
	}

	public async ValueTask DisposeAsync()
	{
		_queue.Writer.TryComplete();
		try
		{
			await _consumer.WaitAsync(_answerTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			await _stop.CancelAsync().ConfigureAwait(false);
		}

		_stop.Dispose();
	}

	internal static string FormatAnswer(long requestId, HelperAnswer answer)
	{
		var text = new StringBuilder(answer.Text.Length + 16);
		text.Append(requestId.ToString(CultureInfo.InvariantCulture))
			.Append('\t')
			.Append(answer.Ok ? "ok" : "error")
			.Append('\t');
		foreach (var character in answer.Text)
		{
			switch (character)
			{
				case '\\':
					text.Append("\\\\");
					break;
				case '\n':
					text.Append("\\n");
					break;
				case '\r':
					text.Append("\\r");
					break;
				default:
					text.Append(character);
					break;
			}
		}

		return text.Append('\n').ToString();
	}

	private async Task ConsumeAsync()
	{
		try
		{
			await foreach (var message in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
			{
				await HandleAsync(message).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task HandleAsync(HelperMessage message)
	{
		try
		{
			switch (message.Kind)
			{
				case HelperMessageKind.SetVariable:
					using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
					{
						timeout.CancelAfter(_answerTimeout);
						await _handler.SetVariableAsync(message.Name, message.Text, timeout.Token)
							.ConfigureAwait(false);
					}

					break;
				case HelperMessageKind.GetVariable:
					await AnswerAsync(message.RequestId, await ReadAsync(message.Name).ConfigureAwait(false))
						.ConfigureAwait(false);
					break;
				case HelperMessageKind.Notify:
					_handler.Notify(message.Title, message.Text, message.Level);
					break;
				case HelperMessageKind.Result:
					_handler.SetResult(message.Text);
					break;
				case HelperMessageKind.Log:
					_handler.Log(message.Text);
					break;
				case HelperMessageKind.Error:
					_handler.ReportError(message);
					break;
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !_stop.IsCancellationRequested)
		{
			_logger.Warning(ex, "An AutoHotkey helper message could not be handled");
			if (message.Kind == HelperMessageKind.GetVariable)
			{
				await AnswerAsync(message.RequestId, new HelperAnswer(false, _handler.NoAnswerText()))
					.ConfigureAwait(false);
			}
		}
	}

	private async Task<HelperAnswer> ReadAsync(string name)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
		timeout.CancelAfter(_answerTimeout);
		try
		{
			return await _handler.GetVariableAsync(name, timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
		{
			return new HelperAnswer(false, _handler.NoAnswerText());
		}
	}

	private async Task AnswerAsync(long requestId, HelperAnswer answer)
	{
		var line = FormatAnswer(requestId, answer);
		await _writeGate.WaitAsync().ConfigureAwait(false);
		try
		{
			await _input.WriteAsync(line).ConfigureAwait(false);
			await _input.FlushAsync().ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
		{
			_logger.Debug(ex, "The AutoHotkey script closed its input before the answer arrived");
		}
		finally
		{
			_writeGate.Release();
		}
	}

	private void ReportDrop(string reason)
	{
		if (_reportedDrop)
		{
			return;
		}

		_reportedDrop = true;
		_logger.Debug("Dropping AutoHotkey helper messages of a run ({Reason})", reason);
	}
}
