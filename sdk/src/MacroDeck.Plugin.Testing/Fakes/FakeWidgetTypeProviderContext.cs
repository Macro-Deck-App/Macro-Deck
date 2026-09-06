using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;
using MacroDeck.Sdk.Widgets;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="IWidgetTypeProviderContext" /> method a <see cref="WidgetTypeProviderCall" />
/// recorded.</summary>
public enum WidgetTypeProviderCallKind
{
	/// <summary>Recorded by <see cref="IWidgetTypeProviderContext.RegisterWidgetTypeAsync" />.</summary>
	Register,

	/// <summary>Recorded by <see cref="IWidgetTypeProviderContext.UnregisterWidgetTypeAsync" />.</summary>
	Unregister
}

/// <summary>One call recorded by <see cref="FakeWidgetTypeProviderContext" />.</summary>
public sealed record WidgetTypeProviderCall
{
	/// <summary>Which method was called.</summary>
	public required WidgetTypeProviderCallKind Kind { get; init; }

	/// <summary>The provider-local widget type id the call addressed.</summary>
	public required string WidgetTypeId { get; init; }

	/// <summary>The descriptor, for a register call.</summary>
	public WidgetTypeDescriptor? WidgetType { get; init; }
}

/// <summary>
/// In-memory <see cref="IWidgetTypeProviderContext" />, standing in for the host's widget type registry.
/// It keeps the same identity and validation rules the host has - a re-registration under a known
/// provider-local id replaces the existing type rather than duplicating it, unregistering an unknown id is
/// a silent no-op, and a descriptor that would leave a widget unreadable is rejected up front - so a
/// provider tested against it sees the same behaviour it would see for real.
/// </summary>
public sealed class FakeWidgetTypeProviderContext : IWidgetTypeProviderContext
{
	private readonly ConcurrentDictionary<string, WidgetTypeDescriptor> _widgetTypes = new(StringComparer.Ordinal);
	private readonly List<WidgetTypeProviderCall> _calls = [];
	private readonly Lock _sync = new();

	/// <summary>Every call made against this context, in order.</summary>
	public IReadOnlyList<WidgetTypeProviderCall> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>The widget types currently registered, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, WidgetTypeDescriptor> WidgetTypes => _widgetTypes;

	public Task<WidgetTypeRegistration> RegisterWidgetTypeAsync(
		WidgetTypeDescriptor widgetType,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(widgetType);
		ArgumentException.ThrowIfNullOrWhiteSpace(widgetType.Id);

		if (widgetType.Name.IsEmpty)
		{
			throw new ArgumentException("The widget type name must not be empty.", nameof(widgetType));
		}

		if (widgetType.DefaultData is { } defaultData && !IsJsonObject(defaultData))
		{
			throw new ArgumentException("DefaultData must be a JSON object.", nameof(widgetType));
		}

		if (widgetType.DataSchema is { } dataSchema && !IsJsonSchema(dataSchema))
		{
			throw new ArgumentException("DataSchema must be a valid JSON Schema.", nameof(widgetType));
		}

		if (widgetType.HasConfiguration && widgetType.DataSchema is null)
		{
			throw new ArgumentException("A widget type that declares configuration must supply a DataSchema.",
				nameof(widgetType));
		}

		// Reusing the id a previous registration was given is what makes this a replace rather than a new
		// type - the host resolves the very same way, by (provider, provider-local id).
		_widgetTypes[widgetType.Id] = widgetType;

		lock (_sync)
		{
			_calls.Add(new WidgetTypeProviderCall
			{
				Kind = WidgetTypeProviderCallKind.Register, WidgetTypeId = widgetType.Id, WidgetType = widgetType
			});
		}

		return Task.FromResult(new WidgetTypeRegistration(widgetType.Id, "test-provider"));
	}

	public Task UnregisterWidgetTypeAsync(string widgetTypeId, CancellationToken cancellationToken = default)
	{
		_widgetTypes.TryRemove(widgetTypeId, out _);

		lock (_sync)
		{
			_calls.Add(new WidgetTypeProviderCall
			{
				Kind = WidgetTypeProviderCallKind.Unregister, WidgetTypeId = widgetTypeId
			});
		}

		return Task.CompletedTask;
	}

	private static bool IsJsonObject(string candidate)
	{
		try
		{
			using var document = JsonDocument.Parse(candidate);
			return document.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool IsJsonSchema(string candidate)
	{
		try
		{
			JsonSchema.FromText(candidate);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
