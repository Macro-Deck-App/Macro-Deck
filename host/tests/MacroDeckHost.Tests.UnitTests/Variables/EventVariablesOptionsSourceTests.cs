using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.OptionsSources;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class EventVariablesOptionsSourceTests
{
	private static VariableEntity Variable(string name, VariableScope scope)
	{
		return new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = name,
			Scope = scope,
			ScopeRefId = scope == VariableScope.Global ? null : Guid.NewGuid().ToString(),
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = string.Empty
		};
	}

	[Test]
	public async Task Lists_only_names_a_variable_event_can_run_for()
	{
		var registry = new VariableRegistry();
		registry.Upsert(Variable("count", VariableScope.Global));
		registry.Upsert(Variable("count", VariableScope.Script));
		registry.Upsert(Variable("countdown_running", VariableScope.Widget));
		registry.Upsert(Variable("script_only", VariableScope.Script));
		registry.Upsert(Variable("automation_only", VariableScope.Automation));

		var result = await new EventVariablesOptionsSource(registry).GetOptionsAsync(null, CancellationToken.None);

		Assert.That(result.Options.Select(option => option.Value),
			Is.EqualTo(new[] { "count", "countdown_running" }));
	}
}
