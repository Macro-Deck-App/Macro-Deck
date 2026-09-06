using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Variables;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class VariablesOptionsSource : IHostOptionsSource
{
	private readonly VariableRegistry _registry;

	public VariablesOptionsSource(VariableRegistry registry)
	{
		_registry = registry;
	}

	public string Id => VariableOptionsSourceIds.Variables;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var names = _registry.GetAll()
			.Select(variable => variable.Name)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.Ordinal)
			.Where(name => filter is null || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = names.Select(name => new ActionParameterOption { Value = name }).ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 5
		});
	}
}
