using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Variables;

public readonly record struct VariableScopeLayer(VariableScope Scope, string ScopeRefId);

public sealed class VariableScopeChain : IEquatable<VariableScopeChain>
{
	private readonly VariableScopeLayer[] _layers;

	private VariableScopeChain(VariableScopeLayer[] layers)
	{
		_layers = layers;
	}

	public static VariableScopeChain Global { get; } = new([]);

	public IReadOnlyList<VariableScopeLayer> Layers => _layers;

	public bool IsGlobal => _layers.Length == 0;

	public static VariableScopeChain Of(VariableScope scope, string? scopeRefId)
	{
		return Global.Append(scope, scopeRefId);
	}

	public static VariableScopeChain For(UserVariableContext context)
	{
		ArgumentNullException.ThrowIfNull(context);

		return Global
			.Append(VariableScope.Script, context.ScriptId)
			.Append(VariableScope.Automation, context.AutomationId)
			.Append(VariableScope.Widget, context.OwnerWidgetId);
	}

	public VariableScopeChain Append(VariableScope scope, string? scopeRefId)
	{
		if (scope == VariableScope.Global || string.IsNullOrWhiteSpace(scopeRefId))
		{
			return this;
		}

		var layer = new VariableScopeLayer(scope, scopeRefId);
		return _layers.Contains(layer) ? this : new VariableScopeChain([.. _layers, layer]);
	}

	public bool Equals(VariableScopeChain? other)
	{
		return other is not null && _layers.AsSpan().SequenceEqual(other._layers);
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as VariableScopeChain);
	}

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var layer in _layers)
		{
			hash.Add(layer);
		}

		return hash.ToHashCode();
	}
}
