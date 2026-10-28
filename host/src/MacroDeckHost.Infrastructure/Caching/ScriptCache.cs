using System.Collections.Concurrent;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Scripts;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Domain.Entities;
using Serilog;

namespace MacroDeckHost.Infrastructure.Caching;

public sealed class ScriptCache : IScriptCache, IDisposable
{
	private readonly IScriptStore _store;
	private readonly ILogger _logger;
	private readonly SecretApprovalLedger? _approvals;
	private readonly ConcurrentDictionary<Guid, ScriptEntity> _scripts = new();
	private readonly SemaphoreSlim _initializationLock = new(1, 1);
	private readonly SemaphoreSlim _updateLock = new(1, 1);
	private bool _isInitialized;
	private bool _disposed;

	public ScriptCache(IScriptStore store, ILogger logger, SecretApprovalLedger? approvals = null)
	{
		_store = store;
		_logger = logger;
		_approvals = approvals;
	}

	public async Task InitializeCache()
	{
		if (_isInitialized)
		{
			return;
		}

		await _initializationLock.WaitAsync();
		try
		{
			if (_isInitialized)
			{
				return;
			}

			foreach (var file in _store.LoadAll())
			{
				_scripts.TryAdd(file.Id, ToEntity(file));
			}

			_approvals?.LoadGroup(SecretApprovalGroup.Scripts,
				_scripts.Values.ToDictionary(entity => entity.Id, ApprovalOwners));

			_isInitialized = true;
		}
		finally
		{
			_initializationLock.Release();
			_logger.Information("Initialized script cache with {ScriptCount} script(s)", _scripts.Count);
		}
	}

	public ScriptEntity? GetById(Guid id)
	{
		_scripts.TryGetValue(id, out var script);
		return script;
	}

	public List<ScriptEntity> GetAll()
	{
		return _scripts.Values.ToList();
	}

	public Task AddOrUpdate(ScriptEntity script)
	{
		return AddOrUpdate(script, SecretApprovalWrite.Preserve);
	}

	public async Task AddOrUpdate(ScriptEntity script, SecretApprovalWrite write)
	{
		await _updateLock.WaitAsync();
		try
		{
			_scripts.AddOrUpdate(script.Id, script, (_, _) => script);
			if (_approvals is not null)
			{
				var owner = FlowOwner.Script(script.Id);
				_approvals.Assign(owner, SecretApprovalGroup.Scripts, script.Id);
				_approvals.Record(owner, FlowSecretBlocks.FromFlows(script.Flows), write);
				_approvals.SavePartition(SecretApprovalGroup.Scripts, script.Id);
			}

			_store.Save(ToFile(script));
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public async Task Remove(Guid id)
	{
		await _updateLock.WaitAsync();
		try
		{
			if (_scripts.TryRemove(id, out _))
			{
				_store.Delete(id);
				_approvals?.DeletePartition(SecretApprovalGroup.Scripts, id);
			}
		}
		finally
		{
			_updateLock.Release();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_initializationLock.Dispose();
		_updateLock.Dispose();
	}

	private static ScriptEntity ToEntity(ScriptFile file)
	{
		return new ScriptEntity
		{
			Id = file.Id,
			Name = file.Name,
			Description = file.Description,
			Flows = file.Flows,
			Inputs = [.. file.Inputs],
			RunsOnWidget = file.RunsOnWidget,
			CreatedAt = file.CreatedAt,
			UpdatedAt = file.UpdatedAt
		};
	}

	private static ScriptFile ToFile(ScriptEntity script)
	{
		return new ScriptFile
		{
			Id = script.Id,
			Name = script.Name,
			Description = script.Description,
			Flows = script.Flows,
			Inputs = [.. script.Inputs],
			RunsOnWidget = script.RunsOnWidget,
			CreatedAt = script.CreatedAt,
			UpdatedAt = script.UpdatedAt
		};
	}

	private static IReadOnlyList<(FlowOwner Owner, string? Flows)> ApprovalOwners(ScriptEntity entity)
	{
		return [(FlowOwner.Script(entity.Id), entity.Flows)];
	}
}
