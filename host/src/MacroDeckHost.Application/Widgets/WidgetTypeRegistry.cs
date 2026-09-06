using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using Mediator;

namespace MacroDeckHost.Application.Widgets;

/// <inheritdoc />
public sealed class WidgetTypeRegistry : IWidgetTypeRegistry
{
	private readonly IReadOnlyList<WidgetTypeCatalogEntry> _builtIn;

	private readonly ConcurrentDictionary<string, WidgetTypeCatalogEntry> _byQualifiedId =
		new(StringComparer.Ordinal);

	/// <summary>Spellings a client might send for a built-in, mapped to the id they mean - see
	/// <see cref="Normalize" />.</summary>
	private readonly Dictionary<string, string> _builtInByNormalized = new(StringComparer.OrdinalIgnoreCase);

	private readonly IPublisher _publisher;

	// The providers are optional and defaulted rather than required: a test builds a registry with none of
	// the built-in providers around at all, and every built-in type simply reports no configuration then -
	// the answer that is literally true until the providers themselves grow a config surface.
	public WidgetTypeRegistry(IPublisher publisher, IEnumerable<IBuiltInWidgetUiProvider>? providers = null)
	{
		_publisher = publisher;

		var configurable = new HashSet<string>(StringComparer.Ordinal);
		foreach (var provider in providers ?? [])
		{
			if (provider.Surfaces.Any(surface =>
				string.Equals(surface.Kind, UiSurfaceKinds.Config, StringComparison.Ordinal)))
			{
				configurable.Add(provider.WidgetTypeId);
			}
		}

		_builtIn =
		[
			.. BuiltInWidgetTypes.All(configurable)
				.Select(descriptor => new WidgetTypeCatalogEntry(descriptor.Id, string.Empty, descriptor)),
		];

		foreach (var entry in _builtIn)
		{
			_builtInByNormalized[Normalize(entry.WidgetTypeId)] = entry.WidgetTypeId;
		}
	}

	public IReadOnlyList<WidgetTypeCatalogEntry> All
		=>
		[
			.. _builtIn,
			.. _byQualifiedId.Values.OrderBy(candidate => candidate.WidgetTypeId, StringComparer.Ordinal),
		];

	public bool IsRegistered(string? id) => TryResolve(id, out _);

	public string? Resolve(string? spelling)
	{
		if (string.IsNullOrEmpty(spelling))
		{
			return null;
		}

		// A qualified id answers to itself and nothing else. The separator-insensitive tolerance below
		// exists for the built-in spellings only, and applying it across owners would let one provider's
		// `ac-me.pkg::gauge` resolve another's `acme.pkg::gauge` - both are valid owner ids, and they
		// normalize to the same key.
		if (spelling.Contains(QualifiedId.Separator, StringComparison.Ordinal))
		{
			return _byQualifiedId.ContainsKey(spelling) ? spelling : null;
		}

		return _builtInByNormalized.GetValueOrDefault(Normalize(spelling));
	}

	public bool TryResolve(string? widgetTypeId, out WidgetTypeCatalogEntry entry)
	{
		entry = null!;

		if (Resolve(widgetTypeId) is not { } resolved)
		{
			return false;
		}

		if (_byQualifiedId.TryGetValue(resolved, out entry!))
		{
			return true;
		}

		entry = _builtIn.First(candidate =>
			string.Equals(candidate.WidgetTypeId, resolved, StringComparison.Ordinal));
		return true;
	}

	public async Task<WidgetTypeRegistration> Register(
		string ownerId,
		WidgetTypeDescriptor widgetType,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(widgetType);

		if (!QualifiedId.TryCreate(ownerId, widgetType.Id, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			throw new ArgumentException(
				$"'{ownerId}' is not a valid owner id, or '{widgetType.Id}' is not a valid widget type id.",
				nameof(widgetType));
		}

		if (widgetType.Name.IsEmpty)
		{
			throw new ArgumentException("The widget type name must not be empty.", nameof(widgetType));
		}

		ValidateDefaultData(widgetType);
		ValidateDataSchema(widgetType);

		var qualifiedId = id.ToString();
		_byQualifiedId[qualifiedId] = new WidgetTypeCatalogEntry(qualifiedId, ownerId, widgetType);

		await _publisher.Publish(new WidgetTypeCatalogChangedNotification(), cancellationToken);

		return new WidgetTypeRegistration(qualifiedId, ownerId);
	}

	public async Task Unregister(string ownerId, string localId, CancellationToken cancellationToken = default)
	{
		if (!QualifiedId.TryCreate(ownerId, localId, OwnerIdKind.Package, LocalIdKind.Resource, out var id))
		{
			return;
		}

		if (_byQualifiedId.TryRemove(id.ToString(), out _))
		{
			await _publisher.Publish(new WidgetTypeCatalogChangedNotification(), cancellationToken);
		}
	}

	public async Task UnregisterAll(string ownerId, CancellationToken cancellationToken = default)
	{
		var removedAny = false;
		foreach (var (key, entry) in _byQualifiedId)
		{
			if (string.Equals(entry.ProviderId, ownerId, StringComparison.Ordinal))
			{
				removedAny |= _byQualifiedId.TryRemove(key, out _);
			}
		}

		if (removedAny)
		{
			await _publisher.Publish(new WidgetTypeCatalogChangedNotification(), cancellationToken);
		}
	}

	private static void ValidateDefaultData(WidgetTypeDescriptor widgetType)
	{
		if (string.IsNullOrWhiteSpace(widgetType.DefaultData))
		{
			return;
		}

		try
		{
			using var document = JsonDocument.Parse(widgetType.DefaultData);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				throw new ArgumentException("The widget type's default data must be a JSON object.",
					nameof(widgetType));
			}
		}
		catch (JsonException exception)
		{
			throw new ArgumentException("The widget type's default data is not valid JSON.",
				nameof(widgetType),
				exception);
		}
	}

	private static void ValidateDataSchema(WidgetTypeDescriptor widgetType)
	{
		if (string.IsNullOrWhiteSpace(widgetType.DataSchema))
		{
			// A tree writing into a payload nothing validates is how a widget's data silently becomes
			// unreadable, with the save succeeding and nothing reporting an error - so a type that offers
			// configuration has to say what its data may look like.
			if (widgetType.HasConfiguration)
			{
				throw new ArgumentException(
					"A widget type that declares configuration must also declare a data schema.",
					nameof(widgetType));
			}

			return;
		}

		try
		{
			if (JsonSchema.FromText(widgetType.DataSchema) is null)
			{
				throw new ArgumentException("The widget type's data schema is not a valid JSON Schema.",
					nameof(widgetType));
			}
		}
		catch (JsonException exception)
		{
			throw new ArgumentException("The widget type's data schema is not a valid JSON Schema.",
				nameof(widgetType),
				exception);
		}
	}

	/// <summary>
	/// A spelling reduced to what identifies it: separators dropped and casing ignored. Clients have
	/// spelled the built-in types both ways for as long as there have been two of them - the deck's own
	/// editor asks for an <c>action-button</c> preview while the stored id reads <c>ActionButton</c> - and
	/// a type that only answers to one spelling silently loses whichever half of the app uses the other.
	/// </summary>
	private static string Normalize(string spelling) => spelling.Replace("-", string.Empty, StringComparison.Ordinal);
}
