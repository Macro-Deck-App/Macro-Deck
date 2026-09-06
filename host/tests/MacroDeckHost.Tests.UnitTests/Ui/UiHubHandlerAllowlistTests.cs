using System.Reflection;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.FolderViews;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class UiHubHandlerAllowlistTests
{
	/// <summary>
	/// The requests the sequential WebSocket dispatcher may answer directly, each one a deliberate call
	/// against ADR 0062 rather than a default. The bar is that the read cannot hold up the rest of a
	/// connection's traffic - answered from host-local state, or reaching a provider under a hard bound.
	/// An unbounded read belongs elsewhere, which is why the music player's catalog and device list are
	/// asserted *off* this list by the tests below rather than merely left absent from it.
	/// </summary>
	private static readonly Type[] _allowedHubHandlerRequests =
	[
		typeof(GetMusicPlayerInstancesRequest),
		typeof(GetMusicPlayerStateRequest),
		typeof(GetWeatherInstancesRequest),
		typeof(GetWeatherStateRequest),
		typeof(GetVariableCatalogProvidersRequest),
		typeof(DiscoverCatalogVariablesRequest),
		typeof(ResolveCatalogVariableRequest),
		typeof(ReportFolderChangedRequest),

		// Reads IFolderViewRegistry's in-memory catalog. No provider is consulted: a provider pushes its
		// registrations, so what the picker needs is already host-local by the time it is asked for.
		typeof(GetFolderViewsRequest),

		// Reads the first-party preview scan and the per-connection plugin capability snapshots, both
		// host-local. No provider is consulted and no scenario is built - a plugin's previews arrived with
		// its ui/describe, and building one is the separate session open.
		typeof(ListUiPreviewsRequest)
	];

	/// <summary>
	/// Both music player pickers are dialogs now, so neither the catalog nor the device list is read over
	/// a transport of its own at all: the picker's own session reads it in process. These two assertions
	/// keep a hand-rolled read off the dispatcher, which is where one would land next.
	/// </summary>
	[Test]
	public void UiHub_does_not_expose_the_catalog()
	{
		Assert.That(DispatcherMethodNames(), Has.None.Contains("Catalog"));
	}

	[Test]
	public void UiHub_does_not_expose_the_device_list()
	{
		Assert.That(DispatcherMethodNames(), Has.None.Contains("GetMusicPlayerDevices"));
	}

	[Test]
	public void UiHub_handler_dependencies_stay_on_the_reviewed_allowlist()
	{
		Assert.That(HubHandlerRequestTypes(), Is.EquivalentTo(_allowedHubHandlerRequests));
	}

	private static List<string> DispatcherMethodNames()
		=> typeof(UiWebSocketDispatcher)
			.GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Select(m => m.Name)
			.ToList();

	private static List<Type> HubHandlerRequestTypes()
		=> typeof(UiWebSocketDispatcher)
			.GetConstructors()
			.Single()
			.GetParameters()
			.Select(p => p.ParameterType)
			.Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IUiTransportMessageHandler<,>))
			.Select(t => t.GetGenericArguments()[0])
			.ToList();
}
