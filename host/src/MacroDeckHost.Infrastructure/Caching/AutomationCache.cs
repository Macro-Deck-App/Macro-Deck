using System.Collections.Concurrent;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Persistence.Automations;
using MacroDeckHost.Domain.Entities;
using Serilog;

namespace MacroDeckHost.Infrastructure.Caching;

public sealed class AutomationCache : IAutomationCache, IDisposable
{
	private readonly IAutomationStore _store;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<Guid, AutomationEntity> _automations = new();
	private readonly SemaphoreSlim _initializationLock = new(1, 1);
	private readonly SemaphoreSlim _updateLock = new(1, 1);
	private bool _isInitialized;
	private bool _disposed;

	public AutomationCache(IAutomationStore store, ILogger logger)
	{
		_store = store;
		_logger = logger;
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
				_automations.TryAdd(file.Id, ToEntity(file));
			}

			_isInitialized = true;
		}
		finally
		{
			_initializationLock.Release();
			_logger.Information("Initialized automation cache with {AutomationCount} automation(s)",
				_automations.Count);
		}
	}

	public AutomationEntity? GetById(Guid id)
	{
		_automations.TryGetValue(id, out var automation);
		return automation;
	}

	public List<AutomationEntity> GetAll() => _automations.Values.ToList();

	public async Task AddOrUpdate(AutomationEntity automation)
	{
		await _updateLock.WaitAsync();
		try
		{
			_automations.AddOrUpdate(automation.Id, automation, (_, _) => automation);
			_store.Save(ToFile(automation));
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
			if (_automations.TryRemove(id, out _))
			{
				_store.Delete(id);
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

	private static AutomationEntity ToEntity(AutomationFile file) => new()
	{
		Id = file.Id,
		Name = file.Name,
		Description = file.Description,
		Enabled = file.Enabled,
		Flows = file.Flows,
		CreatedAt = file.CreatedAt,
		UpdatedAt = file.UpdatedAt
	};

	private static AutomationFile ToFile(AutomationEntity automation) => new()
	{
		Id = automation.Id,
		Name = automation.Name,
		Description = automation.Description,
		Enabled = automation.Enabled,
		Flows = automation.Flows,
		CreatedAt = automation.CreatedAt,
		UpdatedAt = automation.UpdatedAt
	};
}
