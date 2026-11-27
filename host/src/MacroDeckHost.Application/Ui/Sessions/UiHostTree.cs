using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Testing;
using Serilog;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed class UiHostTree : IUiHostObserver, IDisposable
{
	private readonly Lock _gate = new();
	private readonly Action<JsonElement> _changed;
	private readonly Action<UiSessionEndReason, string?, bool> _ended;
	private readonly ILogger _logger;

	private UiTree? _tree;
	private IUiHostObservation? _observation;
	private bool _resyncPending;
	private bool _disposed;

	public UiHostTree(Action<JsonElement> changed, Action<UiSessionEndReason, string?, bool> ended, ILogger logger)
	{
		_changed = changed;
		_ended = ended;
		_logger = logger;
	}

	public static UiHostTree? Observe(
		IUiSessionBroker broker,
		string sessionId,
		Action<JsonElement> changed,
		Action<UiSessionEndReason, string?, bool> ended,
		ILogger logger)
	{
		var tree = new UiHostTree(changed, ended, logger);
		if (broker.ObserveHost(sessionId, tree) is not { } observation)
		{
			return null;
		}

		tree.Attach(observation);
		return tree;
	}

	public void OnSnapshot(UiRawJson tree, int revision)
	{
		lock (_gate)
		{
			if (_disposed || Read<UiTree>(tree) is not { } parsed)
			{
				return;
			}

			_tree = parsed with { Revision = revision };
			_changed(UiCanonicalJson.ToElement(_tree));
		}
	}

	public void OnPatch(UiRawJson patch, int fromRevision, int toRevision)
	{
		lock (_gate)
		{
			if (_disposed || _tree is null)
			{
				return;
			}

			var applied = Read<UiPatch>(patch) is { } parsed ? UiTreeApplier.Apply(_tree, parsed) : null;
			if (applied is not { IsApplied: true })
			{
				_logger.Debug("A patch {FromRevision}->{ToRevision} did not apply to the host's tree: {Reason}",
					fromRevision,
					toRevision,
					applied?.RejectionReason);
				RequestResyncLocked();
				return;
			}

			_tree = applied.Tree;
			_changed(UiCanonicalJson.ToElement(_tree));
		}
	}

	// Outside the gate: the end is terminal, and a reader typically disposes this tree in answer to it.
	public void OnEnded(UiSessionEndReason reason, string? code, bool retryable)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}
		}

		_ended(reason, code, retryable);
	}

	public void Dispose()
	{
		IUiHostObservation? observation;
		lock (_gate)
		{
			_disposed = true;
			observation = _observation;
			_observation = null;
		}

		observation?.Dispose();
	}

	private void Attach(IUiHostObservation observation)
	{
		lock (_gate)
		{
			_observation = observation;
			if (_resyncPending)
			{
				_resyncPending = false;
				observation.RequestResync();
			}
		}
	}

	private void RequestResyncLocked()
	{
		if (_observation is { } observation)
		{
			observation.RequestResync();
		}
		else
		{
			_resyncPending = true;
		}
	}

	private T? Read<T>(UiRawJson payload)
		where T : class
	{
		try
		{
			return JsonSerializer.Deserialize<T>(payload.Utf8.Span, UiCanonicalJson.Options);
		}
		catch (JsonException exception)
		{
			_logger.Debug(exception, "A UI payload could not be read for the host's tree");
			return null;
		}
	}
}
