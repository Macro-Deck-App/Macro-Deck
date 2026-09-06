using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Triggers;

public sealed class EventPreviewSamples
{
	private readonly IEventRegistry _registry;
	private readonly EventSampleStore _samples;

	public EventPreviewSamples(IEventRegistry registry, EventSampleStore samples)
	{
		_registry = registry;
		_samples = samples;
	}

	public VariableContext Overlay(
		VariableContext context,
		string? qualifiedEventId,
		IReadOnlyDictionary<string, object?>? overrides = null)
	{
		if (string.IsNullOrWhiteSpace(qualifiedEventId))
		{
			return context;
		}

		var descriptor = _registry.Find(qualifiedEventId);
		if (descriptor is null)
		{
			return context;
		}

		var values = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var parameter in descriptor.Definition.PayloadParameters)
		{
			values[parameter.Name] = parameter.DefaultValue ?? string.Empty;
		}

		var last = _samples.TryGetLast(qualifiedEventId);
		if (last is not null)
		{
			foreach (var pair in last)
			{
				values[pair.Key] = pair.Value;
			}
		}

		if (overrides is not null)
		{
			foreach (var pair in overrides)
			{
				values[pair.Key] = pair.Value;
			}
		}

		return context.WithEvent(values);
	}
}
