namespace MacroDeckHost.Application.Plugins.Runtime;

public interface IPluginTakeoverRegistry
{
	IReadOnlyCollection<string> Active { get; }

	Guid Begin(string pluginId);

	bool IsCurrent(string pluginId, Guid ticket);

	bool IsActive(string pluginId);

	Guid? CurrentTicket(string pluginId);

	void Finish(string pluginId);
}

public sealed class PluginTakeoverRegistry : IPluginTakeoverRegistry
{
	private readonly Dictionary<string, Guid> _tickets = new(StringComparer.Ordinal);
	private readonly Lock _lock = new();

	public IReadOnlyCollection<string> Active
	{
		get
		{
			lock (_lock)
			{
				return _tickets.Keys.ToList();
			}
		}
	}

	public Guid Begin(string pluginId)
	{
		var ticket = Guid.NewGuid();
		lock (_lock)
		{
			_tickets[pluginId] = ticket;
		}

		return ticket;
	}

	public bool IsCurrent(string pluginId, Guid ticket)
	{
		lock (_lock)
		{
			return _tickets.TryGetValue(pluginId, out var current) && current == ticket;
		}
	}

	public bool IsActive(string pluginId)
	{
		lock (_lock)
		{
			return _tickets.ContainsKey(pluginId);
		}
	}

	public Guid? CurrentTicket(string pluginId)
	{
		lock (_lock)
		{
			return _tickets.TryGetValue(pluginId, out var current) ? current : null;
		}
	}

	public void Finish(string pluginId)
	{
		lock (_lock)
		{
			_tickets.Remove(pluginId);
		}
	}
}
