using MacroDeck.Localization;
using MacroDeck.Sdk.FolderViews;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestFolderViewProviders
{
	/// <summary>A fresh, empty folder view registry - so a test about something else gets the built-in
	/// widget grid and nothing more.</summary>
	public static IFolderViewRegistry Registry() => new FolderViewRegistry(new RecordingMediator());

	/// <summary>A folder-view-provider host wired to a fresh registry, for tests about something else.</summary>
	public static FolderViewProviderHost Host() =>
		new(Registry(), TimeProvider.System, Serilog.Core.Logger.None);

	/// <summary>
	/// A registry already offering one provider's view, and the qualified id a folder would store to
	/// select it.
	/// </summary>
	public static (IFolderViewRegistry Registry, string FolderViewId) WithView(
		string ownerId = "com.example.home",
		string localId = "dashboard",
		bool hasConfiguration = false)
	{
		var registry = Registry();
		var registration = registry
			.Register(ownerId,
				new FolderViewDescriptor(localId,
					LocalizedText.FromLiteral("Dashboard"),
					HasConfiguration: hasConfiguration))
			.GetAwaiter()
			.GetResult();

		return (registry, registration.FolderViewId);
	}
}
