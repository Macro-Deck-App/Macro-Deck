using MacroDeck.Localization;
using MacroDeck.Sdk.ScreenSavers;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Infrastructure.Integrations;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestScreenSaverProviders
{
	public static IScreenSaverRegistry Registry()
	{
		return new ScreenSaverRegistry(new RecordingMediator());
	}

	public static ScreenSaverRegistry RegistryWithBuiltInClock()
	{
		return new ScreenSaverRegistry(new RecordingMediator(), [new BuiltInClock()]);
	}

	internal sealed class BuiltInClock : IScreenSaverProvider, IBuiltInIntegrationUiProvider
	{
		public string IntegrationId => BuiltInScreenSavers.ProviderId;

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces => [];

		public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		public Task InitializeAsync(IScreenSaverProviderContext context, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public IReadOnlyList<ScreenSaverDescriptor> GetScreenSavers()
		{
			return [new ScreenSaverDescriptor("clock", LocalizedText.FromLiteral("Clock"))];
		}
	}

	public static ScreenSaverProviderHost Host()
	{
		return new ScreenSaverProviderHost(Registry(), TimeProvider.System, Serilog.Core.Logger.None);
	}

	public static (IScreenSaverRegistry Registry, string ScreenSaverId) WithScreenSaver(
		string ownerId = "com.example.home",
		string localId = "photos",
		bool hasConfiguration = false,
		bool interactive = false)
	{
		var registry = Registry();
		var registration = registry
			.Register(ownerId,
				new ScreenSaverDescriptor(localId,
					LocalizedText.FromLiteral("Photos"),
					HasConfiguration: hasConfiguration,
					Interactive: interactive))
			.GetAwaiter()
			.GetResult();

		return (registry, registration.ScreenSaverId);
	}
}
