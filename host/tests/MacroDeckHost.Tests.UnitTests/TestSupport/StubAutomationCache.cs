using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class StubAutomationCache : IAutomationCache
{
	private readonly Dictionary<Guid, AutomationEntity> _automations = new();

	public AutomationEntity Add(string? flows, bool enabled = true, string name = "Automation")
	{
		var automation = new AutomationEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Enabled = enabled,
			Flows = flows ?? string.Empty,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};

		_automations[automation.Id] = automation;
		return automation;
	}

	public void Clear() => _automations.Clear();

	public Task InitializeCache() => Task.CompletedTask;

	public AutomationEntity? GetById(Guid id) => _automations.GetValueOrDefault(id);

	public List<AutomationEntity> GetAll() => _automations.Values.ToList();

	public Task AddOrUpdate(AutomationEntity automation)
	{
		_automations[automation.Id] = automation;
		return Task.CompletedTask;
	}

	public Task Remove(Guid id)
	{
		_automations.Remove(id);
		return Task.CompletedTask;
	}
}
