using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public sealed class TemplateVariablesOptionsSource : IHostOptionsSource
{
	private readonly VariableRegistry _registry;

	public TemplateVariablesOptionsSource(VariableRegistry registry)
	{
		_registry = registry;
	}

	public string Id => VariableOptionsSourceIds.TemplateVariables;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var names = _registry.GetAll()
			.Where(variable => variable.TemplateSource is not null)
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
