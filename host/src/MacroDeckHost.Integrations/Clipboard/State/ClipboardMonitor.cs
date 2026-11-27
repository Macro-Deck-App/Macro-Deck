using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Clipboard.Configuration;
using MacroDeckHost.Integrations.Clipboard.Events;
using MacroDeckHost.Integrations.Clipboard.Native;
using Serilog;

namespace MacroDeckHost.Integrations.Clipboard.State;

internal sealed class ClipboardMonitor : IDisposable
{
	public const int PayloadTextLimit = 64 * 1024;

	private const int MaxOwnWrites = 16;
	private const int MaxCachedPatterns = 64;
	private const string EqualsOperator = "==";

	private static readonly TimeSpan _fingerprintWindow = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _matchTimeout = TimeSpan.FromMilliseconds(100);

	private readonly IClipboardBackend _backend;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _chain = new();
	private readonly List<OwnWrite> _ownWrites = [];
	private readonly Dictionary<string, Regex?> _patterns = new(StringComparer.Ordinal);

	private IEventPublisher? _events;
	private IClipboardChangeSource? _source;
	private ClipboardSettings _settings = ClipboardSettings.Off;
	private volatile ClipboardSnapshot? _snapshot;
	private Task _work = Task.CompletedTask;

	public ClipboardMonitor(IClipboardBackend backend, TimeProvider time, ILogger logger)
	{
		_backend = backend;
		_time = time;
		_logger = logger;
	}

	public event Action? StateChanged;

	public ClipboardSnapshot? Snapshot => _snapshot;

	public bool IsWatchUnsupported => _source?.IsWatchUnsupported == true;

	public async Task ApplyAsync(
		ClipboardSettings settings,
		IEventPublisher events,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			_events = events;
			var previous = _settings;
			_settings = settings;

			if (!settings.Watch)
			{
				StopSource();
				_snapshot = null;
				return;
			}

			if (!settings.ShowsText && _snapshot is { } cached)
			{
				_snapshot = cached with { Text = null, TextTooLarge = false };
			}

			if (_source is null)
			{
				if (!_backend.Availability.IsAvailable)
				{
					return;
				}

				var source = _backend.CreateChangeSource();
				source.Changed += OnChanged;
				source.WatchUnsupported += OnWatchUnsupported;
				_source = source;
				source.Start();
				_snapshot = await ReadSeedAsync().ConfigureAwait(false);
			}
			else if (settings.ShowsText && !previous.ShowsText)
			{
				_snapshot = await ReadSeedAsync().ConfigureAwait(false);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<ClipboardWriteReceipt> WriteAsync(
		Func<CancellationToken, Task<ClipboardWriteReceipt>> write,
		ClipboardContentKinds kinds,
		string? text,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var receipt = await write(cancellationToken).ConfigureAwait(false);
			if (_source is not null)
			{
				RememberOwnWrite(receipt, kinds, text);
			}

			return receipt;
		}
		finally
		{
			_gate.Release();
		}
	}

	internal Task WhenIdleAsync()
	{
		lock (_chain)
		{
			return _work;
		}
	}

	public async Task StopAsync()
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			StopSource();
			_snapshot = null;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose()
	{
		StopSource();
		_snapshot = null;
		_gate.Dispose();
	}

	private void OnChanged(long? token)
	{
		lock (_chain)
		{
			_work = HandleAfterAsync(_work, token);
		}
	}

	private void OnWatchUnsupported()
	{
		_logger.Information("This desktop does not let Macro Deck watch the clipboard; clipboard events stay off");
		lock (_chain)
		{
			_work = ForgetAfterAsync(_work);
		}
	}

	private async Task ForgetAfterAsync(Task previous)
	{
		await previous.ConfigureAwait(false);
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			_snapshot = null;
		}
		finally
		{
			_gate.Release();
		}

		StateChanged?.Invoke();
	}

	private async Task HandleAfterAsync(Task previous, long? token)
	{
		await previous.ConfigureAwait(false);
		await HandleAsync(token).ConfigureAwait(false);
	}

	private async Task HandleAsync(long? token)
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_source is null || _events is not { } events)
			{
				return;
			}

			var ownByToken = token is { } changeToken && TakeOwnWrite(changeToken);
			var kinds = await _backend.ReadKindsAsync(CancellationToken.None).ConfigureAwait(false);
			var bindings = events.GetBindings();
			var fingerprintLength = token is null ? PendingTextLength() : null;
			var wantsText = kinds.HasFlag(ClipboardContentKinds.Text)
				&& (_settings.ShowsText || WantsText(bindings) || fingerprintLength is not null);
			var reading = wantsText
				? await _backend.ReadTextAsync(Math.Max(PayloadTextLimit, fingerprintLength ?? 0),
						CancellationToken.None)
					.ConfigureAwait(false)
				: ClipboardTextReading.None;
			var own = ownByToken || token is null && TakeFingerprint(kinds, reading);

			var text = reading.Text is { Length: <= PayloadTextLimit } fitting ? fitting : null;
			var tooLarge = reading.IsTooLarge || reading.Text is not null && text is null;
			var previous = _snapshot;
			var current = new ClipboardSnapshot(kinds,
				_settings.ShowsText ? text : null,
				_settings.ShowsText && tooLarge);
			_snapshot = current;

			_logger.Debug("Clipboard changed to {ContentType} ({TextLength} characters read, own write {Own})",
				current.ContentType,
				reading.Text?.Length ?? 0,
				own);
			StateChanged?.Invoke();

			if (own || previous is null)
			{
				return;
			}

			Publish(events, bindings, previous, current, text);
		}
		catch (ClipboardException exception)
		{
			_logger.Warning("The clipboard could not be read after a change: {Failure}", exception.Failure);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning("Handling a clipboard change failed with {ExceptionType}", exception.GetType().Name);
		}
		finally
		{
			_gate.Release();
		}
	}

	private void Publish(
		IEventPublisher events,
		IReadOnlyList<EventBinding> bindings,
		ClipboardSnapshot previous,
		ClipboardSnapshot current,
		string? text)
	{
		var changed = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[ClipboardEventPayload.ContentType] = current.ContentType
		};
		if (text is not null)
		{
			changed[ClipboardEventPayload.Text] = text;
		}

		events.Publish(ClipboardEventIds.ContentChanged, changed);

		if (text is not null)
		{
			foreach (var pattern in BoundPatterns(bindings))
			{
				if (Match(pattern, text) is { } match)
				{
					events.Publish(ClipboardEventIds.TextMatched,
						new Dictionary<string, object?>(StringComparer.Ordinal)
						{
							[ClipboardEventPayload.Pattern] = pattern,
							[ClipboardEventPayload.Text] = text,
							[ClipboardEventPayload.Match] = match
						});
				}
			}
		}

		if (!string.Equals(previous.ContentType, current.ContentType, StringComparison.Ordinal))
		{
			events.Publish(ClipboardEventIds.ContentTypeChanged,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					[ClipboardEventPayload.ContentType] = current.ContentType,
					[ClipboardEventPayload.PreviousContentType] = previous.ContentType
				});
		}
	}

	private async Task<ClipboardSnapshot?> ReadSeedAsync()
	{
		try
		{
			var kinds = await _backend.ReadKindsAsync(CancellationToken.None).ConfigureAwait(false);
			var reading = _settings.ShowsText && kinds.HasFlag(ClipboardContentKinds.Text)
				? await _backend.ReadTextAsync(PayloadTextLimit, CancellationToken.None).ConfigureAwait(false)
				: ClipboardTextReading.None;
			return IsWatchUnsupported ? null : new ClipboardSnapshot(kinds, reading.Text, reading.IsTooLarge);
		}
		catch (ClipboardException exception)
		{
			_logger.Warning("The clipboard could not be read when watching started: {Failure}", exception.Failure);
			return null;
		}
	}

	private void StopSource()
	{
		if (_source is { } source)
		{
			source.Changed -= OnChanged;
			source.WatchUnsupported -= OnWatchUnsupported;
			source.Dispose();
			_source = null;
		}

		_ownWrites.Clear();
	}

	private void RememberOwnWrite(ClipboardWriteReceipt receipt, ClipboardContentKinds kinds, string? text)
	{
		if (_ownWrites.Count >= MaxOwnWrites)
		{
			_ownWrites.RemoveAt(0);
		}

		_ownWrites.Add(new OwnWrite(receipt.ChangeToken,
			kinds,
			text is null ? null : Hash(text),
			text?.Length ?? 0,
			_time.GetUtcNow()));
	}

	private bool TakeOwnWrite(long token)
	{
		var own = _ownWrites.Any(write => write.Token == token);
		_ownWrites.RemoveAll(write => write.Token is { } written && written <= token);
		return own;
	}

	private int? PendingTextLength()
	{
		PruneFingerprints();
		var lengths = _ownWrites.Where(write => write is { Token: null, TextHash: not null })
			.Select(write => write.TextLength)
			.ToList();
		return lengths.Count == 0 ? null : lengths.Max() + 1;
	}

	private bool TakeFingerprint(ClipboardContentKinds kinds, ClipboardTextReading reading)
	{
		PruneFingerprints();
		var textHash = reading.Text is null ? null : Hash(reading.Text);
		var match = _ownWrites.FirstOrDefault(write => write.Token is null && write.Matches(kinds, textHash));
		return match is not null && _ownWrites.Remove(match);
	}

	private void PruneFingerprints()
	{
		var oldest = _time.GetUtcNow() - _fingerprintWindow;
		_ownWrites.RemoveAll(write => write.Token is null && write.WrittenAt < oldest);
	}

	private static bool WantsText(IReadOnlyList<EventBinding> bindings)
	{
		return bindings.Any(binding => binding.EventId is ClipboardEventIds.ContentChanged
			or ClipboardEventIds.TextMatched);
	}

	private static IEnumerable<string> BoundPatterns(IReadOnlyList<EventBinding> bindings)
	{
		return bindings.Where(binding => binding.EventId == ClipboardEventIds.TextMatched)
			.Select(binding => binding.Parameters.GetValueOrDefault(ClipboardEventPayload.Pattern))
			.Where(value => value is { Operator: EqualsOperator })
			.Select(value => value!.ResolvedValue ?? value.Value)
			.Where(value => value is { ValueKind: JsonValueKind.String })
			.Select(value => value!.Value.GetString())
			.OfType<string>()
			.Where(pattern => pattern.Length > 0)
			.Distinct(StringComparer.Ordinal);
	}

	private string? Match(string pattern, string text)
	{
		if (Compile(pattern) is not { } regex)
		{
			return null;
		}

		try
		{
			var match = regex.Match(text);
			return match.Success ? match.Value : null;
		}
		catch (RegexMatchTimeoutException)
		{
			_logger.Debug("A clipboard text pattern timed out on {TextLength} characters", text.Length);
			return null;
		}
	}

	private Regex? Compile(string pattern)
	{
		if (_patterns.TryGetValue(pattern, out var cached))
		{
			return cached;
		}

		if (_patterns.Count >= MaxCachedPatterns)
		{
			_patterns.Clear();
		}

		Regex? regex;
		try
		{
			regex = new Regex(pattern, RegexOptions.CultureInvariant, _matchTimeout);
		}
		catch (ArgumentException)
		{
			regex = null;
		}

		_patterns[pattern] = regex;
		return regex;
	}

	private static byte[] Hash(string text)
	{
		return SHA256.HashData(Encoding.UTF8.GetBytes(text));
	}

	private sealed record OwnWrite(
		long? Token,
		ClipboardContentKinds Kinds,
		byte[]? TextHash,
		int TextLength,
		DateTimeOffset WrittenAt)
	{
		public bool Matches(ClipboardContentKinds observed, byte[]? observedTextHash)
		{
			if (Kinds == ClipboardContentKinds.None)
			{
				return observed == ClipboardContentKinds.None;
			}

			if (!observed.HasFlag(Kinds))
			{
				return false;
			}

			return TextHash is null
				|| observedTextHash is not null && TextHash.AsSpan().SequenceEqual(observedTextHash);
		}
	}
}
