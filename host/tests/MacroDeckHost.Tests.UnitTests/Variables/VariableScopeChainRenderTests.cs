using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableScopeChainRenderTests
{
	private const string ScriptId = "33333333-3333-3333-3333-333333333333";
	private const string WidgetId = "11111111-1111-1111-1111-111111111111";

	private VariableRegistry _registry = null!;
	private VariableTemplateRenderer _renderer = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_renderer = new VariableTemplateRenderer(_registry);
		Seed(VariableScope.Global, null, "global");
		Seed(VariableScope.Widget, WidgetId, "widget");
		Seed(VariableScope.Script, ScriptId, "script");
	}

	[Test]
	public void Each_chain_renders_its_own_most_local_value_even_for_the_same_template()
	{
		var scriptOnWidget = VariableScopeChain.Of(VariableScope.Script, ScriptId)
			.Append(VariableScope.Widget, WidgetId);
		var widgetOnly = VariableScopeChain.Of(VariableScope.Widget, WidgetId);

		var rendered = new[]
		{
			_renderer.Render("{{ vars.x }}", scriptOnWidget),
			_renderer.Render("{{ vars.x }}", widgetOnly),
			_renderer.Render("{{ vars.x }}", VariableScopeChain.Global),
			_renderer.Render("{{ vars.x }}",
				VariableScopeChain.Of(VariableScope.Script, ScriptId).Append(VariableScope.Widget, WidgetId))
		};

		Assert.That(rendered, Is.EqualTo(new[] { "script", "widget", "global", "script" }));
	}

	[Test]
	public async Task A_condition_context_resolves_the_most_local_layer_first()
	{
		var context = await _renderer.CreateContextAsync(VariableScopeChain.Of(VariableScope.Script, ScriptId)
			.Append(VariableScope.Widget, WidgetId));

		Assert.Multiple(() =>
		{
			Assert.That(context.TryResolve("x", out var variable), Is.True);
			Assert.That(variable.Value, Is.EqualTo("script"));
		});
	}

	private void Seed(VariableScope scope, string? scopeRefId, string value)
	{
		_registry.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "x",
			Scope = scope,
			ScopeRefId = scopeRefId,
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = value
		});
	}
}
