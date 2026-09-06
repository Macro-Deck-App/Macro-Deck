using System.Diagnostics;
using MacroDeckHost.Application.Actions.Options;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Infrastructure.OptionsSources;

public class ProcessesOptionsSource : IHostOptionsSource
{
	public string Id => "system.processes";

	public Task<DynamicOptionsResult> GetOptionsAsync(string? filter, CancellationToken cancellationToken)
	{
		var names = Process.GetProcesses()
			.Select(p => p.ProcessName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.OrdinalIgnoreCase)
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
