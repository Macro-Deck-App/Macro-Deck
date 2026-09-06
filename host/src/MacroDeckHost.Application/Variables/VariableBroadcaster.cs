using System.Collections.Concurrent;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Variables;

public sealed class VariableBroadcaster
{
	private const int ChunkSize = 200;

	private readonly VariableRegistry _registry;
	private readonly VariableInterestTracker _interest;
	private readonly IUiTransport _transport;
	private readonly VariableBindingLookup _bindings;
	private readonly ILogger _logger;

	// A deleted id is already gone from VariableRegistry by the time a coalesced batch reaches Publish,
	// so gating its deletion to the connections that declared interest in its name needs the name
	// remembered from the last time this id was seen alive.
	private readonly ConcurrentDictionary<Guid, string> _lastKnownNames = new();

	public VariableBroadcaster(
		VariableRegistry registry,
		VariableInterestTracker interest,
		IUiTransport transport,
		VariableBindingLookup bindings,
		ILogger logger)
	{
		_registry = registry;
		_interest = interest;
		_transport = transport;
		_bindings = bindings;
		_logger = logger.ForContext<VariableBroadcaster>();
	}

	public VariablesChangedEvent Snapshot(IReadOnlyCollection<string> names)
	{
		var wanted = new HashSet<string>(names, StringComparer.Ordinal);
		var evt = new VariablesChangedEvent();

		foreach (var entity in _registry.GetAll())
		{
			if (!wanted.Contains(entity.Name))
			{
				continue;
			}

			_lastKnownNames[entity.Id] = entity.Name;
			var boundResourceId = _bindings.FindByVariableId(entity.Id)?.LocalResourceId;
			evt.Upserted.Add(VariableDtoMapper.ToDto(entity, _registry.IsAvailable(entity.Id), boundResourceId));
		}

		return evt;
	}

	public async Task Publish(IReadOnlyCollection<Guid> ids, CancellationToken ct)
	{
		var upserted = new List<Variable>();
		var deletedIds = new List<string>();
		var deletedNamesById = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var id in ids)
		{
			var entity = _registry.GetById(id);
			if (entity is null)
			{
				deletedIds.Add(id.ToString());
				if (_lastKnownNames.TryRemove(id, out var lastKnownName))
				{
					deletedNamesById[id.ToString()] = lastKnownName;
				}

				continue;
			}

			_lastKnownNames[id] = entity.Name;
			var boundResourceId = _bindings.FindByVariableId(id)?.LocalResourceId;
			upserted.Add(VariableDtoMapper.ToDto(entity, _registry.IsAvailable(id), boundResourceId));
		}

		if (upserted.Count == 0 && deletedIds.Count == 0)
		{
			return;
		}

		// Recipients are sent concurrently: a client that hangs rather than throws is not caught by the
		// per-send guards below, and awaiting recipients in turn would let one such client stall delivery
		// to every other one. Chunks within a single recipient stay sequential, which is what preserves
		// their order on that connection.
		var sends = new List<Task>
		{
			SendChunks(upserted, deletedIds, chunk => SendToGroup(chunk, ct))
		};

		foreach (var (connectionId, names) in _interest.Snapshot())
		{
			var filteredUpserted = upserted.Where(v => names.Contains(v.Name)).ToList();
			var filteredDeletedIds = deletedIds
				.Where(id => deletedNamesById.TryGetValue(id, out var name) && names.Contains(name))
				.ToList();

			if (filteredUpserted.Count == 0 && filteredDeletedIds.Count == 0)
			{
				continue;
			}

			sends.Add(SendChunks(filteredUpserted,
				filteredDeletedIds,
				chunk => SendToConnection(connectionId, chunk, ct)));
		}

		await Task.WhenAll(sends);
	}

	private async Task SendToGroup(VariablesChangedEvent message, CancellationToken ct)
	{
		try
		{
			await _transport.SendToGroup(VariableGroups.WatchAll, message, ct);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to broadcast variable changes to group {Group}", VariableGroups.WatchAll);
		}
	}

	private async Task SendToConnection(string connectionId, VariablesChangedEvent message, CancellationToken ct)
	{
		try
		{
			await _transport.SendToConnection(connectionId, message, ct);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to send variable changes to connection {ConnectionId}", connectionId);
		}
	}

	private static async Task SendChunks(
		List<Variable> upserted,
		List<string> deletedIds,
		Func<VariablesChangedEvent, Task> send)
	{
		var upsertedIndex = 0;
		var deletedIndex = 0;

		while (upsertedIndex < upserted.Count || deletedIndex < deletedIds.Count)
		{
			var chunk = new VariablesChangedEvent();
			var remaining = ChunkSize;

			while (remaining > 0 && upsertedIndex < upserted.Count)
			{
				chunk.Upserted.Add(upserted[upsertedIndex++]);
				remaining--;
			}

			while (remaining > 0 && deletedIndex < deletedIds.Count)
			{
				chunk.DeletedIds.Add(deletedIds[deletedIndex++]);
				remaining--;
			}

			await send(chunk);
		}
	}
}
