using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Variables;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Infrastructure.OptionsSources;

/// <summary>
/// The variables the Set Variable action can write - user variables, and any provider variable whose
/// owner declares a write capability (issue #820). Named for user variables because that is what it
/// offered before provider variables could be written at all, and the id is a contract an action
/// parameter refers to.
/// </summary>
/// <remarks>
/// Writability, not ownership: an integration's volume is as settable as a counter the user made.
/// Add/Toggle/Append still refuse anything but a user variable - see <c>UserVariableWriter</c> - so a
/// provider variable offered here accepts being set and says so plainly for the rest.
/// </remarks>
public sealed class UserVariablesOptionsSource : IHostOptionsSource
{
	private readonly VariableRegistry _registry;

	public UserVariablesOptionsSource(VariableRegistry registry)
	{
		_registry = registry;
	}

	public string Id => VariableOptionsSourceIds.UserVariables;

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var names = _registry.GetAll()
			.Where(variable => variable.CanWrite)
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
