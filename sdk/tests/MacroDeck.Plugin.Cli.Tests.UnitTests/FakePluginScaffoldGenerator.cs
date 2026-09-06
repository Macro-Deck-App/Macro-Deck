using MacroDeck.Plugin.Cli.Scaffolding;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// The template seam's test double - no test using this may invoke real <c>dotnet new</c>, install a
/// template, or touch the network. <see cref="Calls" /> records the order every method was invoked in, for
/// the ordering assertions <c>PluginScaffolderTests</c>' f1/f3 scenarios need.
/// </summary>
internal sealed class FakePluginScaffoldGenerator : IPluginScaffoldGenerator
{
	public List<string> Calls { get; } = [];

	public PluginTemplateStatus ProbeResult { get; set; } = PluginTemplateStatus.UpToDate;

	public PluginTemplateOperationResult InstallResult { get; set; } = new(true, null);

	public PluginTemplateOperationResult UpdateResult { get; set; } = new(true, null);

	public PluginTemplateOperationResult CreateResult { get; set; } = new(true, null);

	public Exception? ThrowOn { get; set; }

	/// <summary>Simulates whatever the real template wrote into the request's output directory before
	/// <see cref="CreateResult" /> is returned - including a partial tree ahead of a failing result, the way
	/// a real <c>dotnet new</c> invocation can leave files behind even when it ultimately fails.</summary>
	public Action<PluginScaffoldRequest>? OnCreate { get; set; }

	public Task<PluginTemplateStatus> ProbeAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(ProbeAsync));
		if (ThrowOn is not null)
		{
			throw ThrowOn;
		}

		return Task.FromResult(ProbeResult);
	}

	public Task<PluginTemplateOperationResult> InstallAsync(string? templateVersion,
		CancellationToken cancellationToken)
	{
		Calls.Add(nameof(InstallAsync));
		if (ThrowOn is not null)
		{
			throw ThrowOn;
		}

		return Task.FromResult(InstallResult);
	}

	public Task<PluginTemplateOperationResult> UpdateAsync(CancellationToken cancellationToken)
	{
		Calls.Add(nameof(UpdateAsync));
		if (ThrowOn is not null)
		{
			throw ThrowOn;
		}

		return Task.FromResult(UpdateResult);
	}

	public Task<PluginTemplateOperationResult> CreateAsync(PluginScaffoldRequest request,
		CancellationToken cancellationToken)
	{
		Calls.Add(nameof(CreateAsync));
		if (ThrowOn is not null)
		{
			throw ThrowOn;
		}

		OnCreate?.Invoke(request);
		return Task.FromResult(CreateResult);
	}
}
