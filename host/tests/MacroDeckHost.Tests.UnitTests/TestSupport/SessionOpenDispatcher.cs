using System.Security.Claims;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestStartupReadiness
{
	public static StartupReadiness Ready()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}
}

internal static class SessionOpenDispatcher
{
	public static UiWebSocketDispatcher For(
		StartupReadiness readiness,
		IWidgetUiSessionOpener? widgets = null,
		IFolderUiSessionOpener? folders = null,
		IConfigUiSessionOpener? config = null)
	{
		return new UiWebSocketDispatcher("connection-1",
			new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope)],
				"test")),
			static () =>
			{
			},
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			getWeatherState: null!,
			getVariableCatalogProviders: null!,
			discoverCatalogVariables: null!,
			resolveCatalogVariable: null!,
			reportFolderChanged: null!,
			listUiPreviews: null!,
			logSubscriptions: null!,
			logFileReader: null!,
			deviceConnections: null!,
			musicPlayerClientSync: null!,
			uiSessions: null!,
			configUiSessions: config!,
			widgetUiSessions: widgets!,
			uiPreviewSessions: null!,
			folderUiSessions: folders!,
			getFolderViews: null!,
			getScreenSavers: null!,
			getDeviceScreenSaver: null!,
			getDeviceClientSettings: null!,
			setDeviceClientSettings: null!,
			screenSaverUiSessions: null!,
			modalUiSessions: null!,
			lifetime: null!,
			transport: null!,
			webSocketTransport: new WebSocketUiTransport(),
			companions: null!,
			licenses: null!,
			accessTokenCutoff: new AccessTokenCutoff(),
			deviceSessionGuard: new DeviceSessionGuard(),
			videoStreams: null!,
			videoStreamProviders: null!,
			notifications: new NullNotificationManager(),
			clientFeatures: new UiClientFeatures(),
			startupReadiness: readiness,
			connectionCancellation: CancellationToken.None);
	}

	public static async Task<TResponse> Open<TResponse>(
		UiWebSocketDispatcher dispatcher,
		string type,
		object request)
	{
		var response = await dispatcher.DispatchAsync(type,
			JsonSerializer.SerializeToElement(new[] { request }, UiWebSocketProtocol.Json),
			CancellationToken.None);
		return response!.Value.Deserialize<TResponse>(UiWebSocketProtocol.Json)!;
	}
}
