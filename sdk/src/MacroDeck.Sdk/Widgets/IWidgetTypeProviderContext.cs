namespace MacroDeck.Sdk.Widgets;

/// <summary>
/// The host surface a widget type provider registers against. Handed to
/// <see cref="IWidgetTypeProvider.InitializeAsync" /> and safe to retain for as long as the integration
/// runs.
/// </summary>
public interface IWidgetTypeProviderContext
{
	/// <summary>
	/// Registers a widget type, or replaces one already registered under the same provider-local id.
	/// Replacing is how a type's name, description, default data, schema or configuration flag changes:
	/// widgets already placed on a deck pick the new descriptor up without being touched.
	/// </summary>
	/// <returns>The host-assigned identity, whose
	/// <see cref="WidgetTypeRegistration.WidgetTypeId" /> is what a widget stores as its type.</returns>
	/// <exception cref="ArgumentException">
	/// The descriptor's id or name is empty, the id is not a valid local id, the default data is not a JSON
	/// object, the data schema is not a valid JSON Schema, or the type declares configuration without one.
	/// </exception>
	Task<WidgetTypeRegistration> RegisterWidgetTypeAsync(
		WidgetTypeDescriptor widgetType,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a widget type. Widgets of that type keep their stored type and data and draw nothing until
	/// it is registered again, so withdrawing never destroys a deck. Unknown ids are ignored, so a provider
	/// racing a shutdown does not have to guard the call.
	/// </summary>
	/// <param name="widgetTypeId">The provider-local id the type was registered under.</param>
	Task UnregisterWidgetTypeAsync(string widgetTypeId, CancellationToken cancellationToken = default);
}
