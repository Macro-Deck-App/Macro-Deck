using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Domain.Entities;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

/// <summary>
/// A dynamic-variable provider whose resources, values and subscribe results are all configured by the
/// test. Call-recording (<see cref="SubscribeCalls"/>, <see cref="GetValueCalls"/>) is what lets a test
/// assert "only bound resources are subscribed" and "a subscribe result seeds without a second read"
/// positively rather than by absence of a mock verification framework.
/// </summary>
internal sealed class FakeVariableProviderIntegration : IIntegration, IVariableProvider
{
	private readonly Dictionary<string, VariableDefinition> _definitions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

	public string Id { get; init; } = "com.example.smart-home";

	public LocalizedText Name { get; init; } = "Smart Home";

	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; init; } = [];

	public bool IsInitialized { get; set; } = true;

	public IReadOnlyList<VariableDefinition> Variables => [];

	public bool SupportsCatalog => true;

	public bool SupportsPush { get; set; }

	public bool SupportsSearch { get; set; }

	public IVariableSink? AttachedSink { get; private set; }

	public int AttachCount { get; private set; }

	public List<List<string>> SubscribeCalls { get; } = [];

	public List<string> GetValueCalls { get; } = [];

	/// <summary>Overrides what <see cref="SubscribeAsync"/> returns for a given call. Ids absent from the
	/// result exercise the host's read-fallback.</summary>
	public Func<IReadOnlyCollection<string>, IReadOnlyList<VariableValue>>? SubscribeResponder { get; set; }

	public void AddDefinition(VariableDefinition definition) => _definitions[definition.Id!] = definition;

	public void SetValue(string id, object? value) => _values[id] = value;

	public Task InitializeAsync(IIntegrationContext context)
	{
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		IsInitialized = false;
		return Task.CompletedTask;
	}

	public ValueTask<VariableCatalogPage> DiscoverAsync(VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(new VariableCatalogPage { Items = _definitions.Values.ToList() });

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(_definitions.GetValueOrDefault(localId));

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		GetValueCalls.Add(localId);
		return ValueTask.FromResult(_values.TryGetValue(localId, out var value)
			? VariableReading.Of(value)
			: VariableReading.Unavailable);
	}

	public ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken = default)
	{
		SubscribeCalls.Add(localIds.ToList());
		var result = SubscribeResponder?.Invoke(localIds) ?? [];
		return ValueTask.FromResult(result);
	}

	public Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken = default)
	{
		AttachedSink = sink;
		AttachCount++;
		return Task.CompletedTask;
	}
}

internal sealed class InMemoryVariableBindingStore : IVariableBindingStore
{
	private List<VariableBinding> _bindings = [];

	/// <summary>When set, the next <see cref="TryLoad"/> reports a failed read (as if the file existed but
	/// could not be recovered) instead of returning the in-memory state, and resets itself afterward.</summary>
	public bool FailNextLoad { get; set; }

	/// <summary>When set, the next <see cref="Save"/> reports a failed write without mutating the
	/// in-memory state, and resets itself afterward.</summary>
	public bool FailNextSave { get; set; }

	public IReadOnlyList<VariableBinding> Load()
	{
		TryLoad(out var bindings);
		return bindings;
	}

	public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
	{
		if (FailNextLoad)
		{
			FailNextLoad = false;
			bindings = [];
			return false;
		}

		bindings = _bindings.ToList();
		return true;
	}

	public bool Save(IEnumerable<VariableBinding> bindings)
	{
		if (FailNextSave)
		{
			FailNextSave = false;
			return false;
		}

		_bindings = bindings.ToList();
		return true;
	}
}

internal sealed class NullUserVariableStore : IUserVariableStore
{
	public IReadOnlyList<VariableEntity> Load() => [];

	public void Save(IEnumerable<VariableEntity> userVariables)
	{
	}
}

internal sealed class StartedHostLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
{
	public CancellationToken ApplicationStarted { get; } = new(true);
	public CancellationToken ApplicationStopping { get; } = new(true);
	public CancellationToken ApplicationStopped { get; } = new(true);

	public void StopApplication()
	{
	}
}
