using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Widgets.Preview;

/// <summary>
/// The sample a widget draws while somebody is choosing a widget type: representative content, resolved
/// without reading anything live. Nothing is configured yet at that moment - no weather station, no music
/// provider, no variable, no bound action - so a provider that read its usual sources would draw its
/// "not set up" state in the picker, which says nothing about what the widget looks like.
/// </summary>
public static class WidgetSamplePreview
{
	/// <summary>Whether this surface asks for the sample rather than live state.</summary>
	public static bool IsRequested(UiSurface surface)
	{
		ArgumentNullException.ThrowIfNull(surface);

		return surface.Kind == UiSurfaceKinds.Preview &&
			surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var sample) &&
			sample.ValueKind == JsonValueKind.True;
	}
}

/// <summary>
/// Resolves the words in a sample into the language the host is set to. Sample text reaches the reader as
/// a widget's own content - a track name, a location - rather than as a UI label, and those fields carry
/// literal text all the way down, so the reference is resolved here the same way a launcher button's label
/// is resolved when one is created from a dropped application.
/// </summary>
public interface IWidgetSampleTextResolver
{
	ValueTask<string> ResolveAsync(LocalizedString value);
}

public sealed class WidgetSampleTextResolver : IWidgetSampleTextResolver
{
	private readonly ILocalizationResolver _localization;
	private readonly IServiceScopeFactory _scopeFactory;

	// Scoped rather than injected: the widget providers this serves are singletons, and the preference
	// service is scoped, so the language has to be read inside a scope of this call's own.
	public WidgetSampleTextResolver(ILocalizationResolver localization, IServiceScopeFactory scopeFactory)
	{
		_localization = localization;
		_scopeFactory = scopeFactory;
	}

	public async ValueTask<string> ResolveAsync(LocalizedString value)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		var culture = (await preferences.GetLocalization().ConfigureAwait(false)).Culture;

		return _localization.Resolve(value, culture);
	}
}
