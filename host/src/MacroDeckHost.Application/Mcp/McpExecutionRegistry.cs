namespace MacroDeckHost.Application.Mcp;

public interface IMcpExecutionRegistry
{
	IDisposable Enter(string integrationId, IReadOnlyCollection<string>? consentedVariables = null);

	bool IsActive(string integrationId);

	bool HasConsent(string integrationId, string variableName);
}

public sealed class McpExecutionRegistry : IMcpExecutionRegistry
{
	public static readonly TimeSpan WriteGracePeriod = TimeSpan.FromSeconds(30);

	private readonly TimeProvider _timeProvider;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Activity> _activities = new(StringComparer.Ordinal);

	public McpExecutionRegistry(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
	}

	public IDisposable Enter(string integrationId, IReadOnlyCollection<string>? consentedVariables = null)
	{
		var consent = consentedVariables?.ToHashSet(StringComparer.Ordinal) ?? [];
		lock (_gate)
		{
			if (!_activities.TryGetValue(integrationId, out var activity))
			{
				activity = _activities[integrationId] = new Activity();
			}

			activity.Running.Add(consent);
		}

		return new Lease(this, integrationId, consent);
	}

	public bool IsActive(string integrationId)
	{
		lock (_gate)
		{
			return _activities.TryGetValue(integrationId, out var activity)
				&& (activity.Running.Count > 0 || activity.Grace.Any(grace => grace.Until > Now()));
		}
	}

	public bool HasConsent(string integrationId, string variableName)
	{
		lock (_gate)
		{
			// A write cannot be traced to one execution, so consent counts only when every MCP execution that
			// may have made it, running or in its grace period, carries it.
			if (!_activities.TryGetValue(integrationId, out var activity))
			{
				return false;
			}

			var now = Now();
			return activity.Running.All(set => set.Contains(variableName))
				&& activity.Grace.Where(grace => grace.Until > now).All(grace => grace.Consent.Contains(variableName));
		}
	}

	private void Exit(string integrationId, HashSet<string> consent)
	{
		lock (_gate)
		{
			if (!_activities.TryGetValue(integrationId, out var activity))
			{
				return;
			}

			var now = Now();
			activity.Running.Remove(consent);
			activity.Grace.RemoveAll(grace => grace.Until <= now);
			activity.Grace.Add((now + WriteGracePeriod, consent));
		}
	}

	private DateTime Now()
	{
		return _timeProvider.GetUtcNow().UtcDateTime;
	}

	private sealed class Activity
	{
		public List<HashSet<string>> Running { get; } = [];

		public List<(DateTime Until, HashSet<string> Consent)> Grace { get; } = [];
	}

	private sealed class Lease : IDisposable
	{
		private readonly McpExecutionRegistry _registry;
		private readonly string _integrationId;
		private readonly HashSet<string> _consent;
		private int _disposed;

		public Lease(McpExecutionRegistry registry, string integrationId, HashSet<string> consent)
		{
			_registry = registry;
			_integrationId = integrationId;
			_consent = consent;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				_registry.Exit(_integrationId, _consent);
			}
		}
	}
}
