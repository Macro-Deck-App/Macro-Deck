using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetAppearanceSettingsRequest, GetAppearanceSettingsResponse>
		_getAppearance;

	private readonly IUiTransportMessageHandler<UpdateAppearanceSettingsRequest, UpdateAppearanceSettingsResponse>
		_updateAppearance;

	private readonly IUiTransportMessageHandler<GetAutostartSettingsRequest, GetAutostartSettingsResponse>
		_getAutostart;

	private readonly IUiTransportMessageHandler<UpdateAutostartSettingsRequest, UpdateAutostartSettingsResponse>
		_updateAutostart;

	private readonly IUiTransportMessageHandler<GetHttpSettingsRequest, GetHttpSettingsResponse> _getHttp;

	private readonly IUiTransportMessageHandler<UpdateHttpSettingsRequest, UpdateHttpSettingsResponse> _updateHttp;

	private readonly IUiTransportMessageHandler<GetLoggingSettingsRequest, GetLoggingSettingsResponse>
		_getLogging;

	private readonly IUiTransportMessageHandler<UpdateLoggingSettingsRequest, UpdateLoggingSettingsResponse>
		_updateLogging;

	private readonly IUiTransportMessageHandler<GetNetworkSettingsRequest, GetNetworkSettingsResponse>
		_getNetwork;

	private readonly IUiTransportMessageHandler<UpdateNetworkSettingsRequest, UpdateNetworkSettingsResponse>
		_updateNetwork;

	private readonly IUiTransportMessageHandler<UpdateNetworkTlsCertificateRequest, UpdateNetworkSettingsResponse>
		_updateNetworkTlsCertificate;

	private readonly IUiTransportMessageHandler<ReissueTlsCertificateRequest, UpdateNetworkSettingsResponse>
		_reissueTlsCertificate;

	private readonly IUiTransportMessageHandler<RegenerateTlsCertificateAuthorityRequest,
		UpdateNetworkSettingsResponse> _regenerateTlsCertificateAuthority;

	private readonly IUiTransportMessageHandler<GetAdbSettingsRequest, GetAdbSettingsResponse> _getAdb;

	private readonly IUiTransportMessageHandler<UpdateAdbSettingsRequest, UpdateAdbSettingsResponse> _updateAdb;

	private readonly IUiTransportMessageHandler<RestartAdbServerRequest, RestartAdbServerResponse> _restartAdbServer;
	private readonly IUiTransportMessageHandler<ConnectAdbDeviceRequest, ConnectAdbDeviceResponse> _connectAdbDevice;

	private readonly IUiTransportMessageHandler<DownloadAdbPlatformToolsRequest, DownloadAdbPlatformToolsResponse>
		_downloadAdbPlatformTools;

	private readonly IUiTransportMessageHandler<GetNativeUsbSettingsRequest, GetNativeUsbSettingsResponse>
		_getNativeUsb;

	private readonly IUiTransportMessageHandler<UpdateNativeUsbSettingsRequest, UpdateNativeUsbSettingsResponse>
		_updateNativeUsb;

	private readonly IUiTransportMessageHandler<RefreshNativeUsbSettingsRequest, GetNativeUsbSettingsResponse>
		_refreshNativeUsb;

	private readonly IUiTransportMessageHandler<ConnectNativeUsbDeviceRequest, ConnectNativeUsbDeviceResponse>
		_connectNativeUsbDevice;

	private readonly IUiTransportMessageHandler<ForgetNativeUsbDeviceRequest, ForgetNativeUsbDeviceResponse>
		_forgetNativeUsbDevice;

	private readonly IUiTransportMessageHandler<GetDeveloperSettingsRequest, GetDeveloperSettingsResponse>
		_getDeveloper;

	private readonly IUiTransportMessageHandler<UpdateDeveloperSettingsRequest, UpdateDeveloperSettingsResponse>
		_updateDeveloper;

	private readonly IUiTransportMessageHandler<GetOnboardingStateRequest, GetOnboardingStateResponse>
		_getOnboarding;

	private readonly IUiTransportMessageHandler<CompleteOnboardingRequest, CompleteOnboardingResponse>
		_completeOnboarding;

	private readonly IUiTransportMessageHandler<GetLockScreenSettingsRequest, GetLockScreenSettingsResponse>
		_getLockScreen;

	private readonly IUiTransportMessageHandler<UpdateLockScreenSettingsRequest, UpdateLockScreenSettingsResponse>
		_updateLockScreen;

	private readonly IUiTransportMessageHandler<GetExtensionSettingsRequest, GetExtensionSettingsResponse>
		_getExtensions;

	private readonly IUiTransportMessageHandler<UpdateExtensionSettingsRequest, UpdateExtensionSettingsResponse>
		_updateExtensions;

	private readonly IUiTransportMessageHandler<GetLocalizationSettingsRequest, GetLocalizationSettingsResponse>
		_getLocalization;

	private readonly IUiTransportMessageHandler<UpdateLocalizationSettingsRequest, UpdateLocalizationSettingsResponse>
		_updateLocalization;

	public SettingsController(
		IUiTransportMessageHandler<GetAppearanceSettingsRequest, GetAppearanceSettingsResponse> getAppearance,
		IUiTransportMessageHandler<UpdateAppearanceSettingsRequest, UpdateAppearanceSettingsResponse> updateAppearance,
		IUiTransportMessageHandler<GetAutostartSettingsRequest, GetAutostartSettingsResponse> getAutostart,
		IUiTransportMessageHandler<UpdateAutostartSettingsRequest, UpdateAutostartSettingsResponse> updateAutostart,
		IUiTransportMessageHandler<GetHttpSettingsRequest, GetHttpSettingsResponse> getHttp,
		IUiTransportMessageHandler<UpdateHttpSettingsRequest, UpdateHttpSettingsResponse> updateHttp,
		IUiTransportMessageHandler<GetLoggingSettingsRequest, GetLoggingSettingsResponse> getLogging,
		IUiTransportMessageHandler<UpdateLoggingSettingsRequest, UpdateLoggingSettingsResponse> updateLogging,
		IUiTransportMessageHandler<GetNetworkSettingsRequest, GetNetworkSettingsResponse> getNetwork,
		IUiTransportMessageHandler<UpdateNetworkSettingsRequest, UpdateNetworkSettingsResponse> updateNetwork,
		IUiTransportMessageHandler<UpdateNetworkTlsCertificateRequest, UpdateNetworkSettingsResponse>
			updateNetworkTlsCertificate,
		IUiTransportMessageHandler<ReissueTlsCertificateRequest, UpdateNetworkSettingsResponse>
			reissueTlsCertificate,
		IUiTransportMessageHandler<RegenerateTlsCertificateAuthorityRequest, UpdateNetworkSettingsResponse>
			regenerateTlsCertificateAuthority,
		IUiTransportMessageHandler<GetAdbSettingsRequest, GetAdbSettingsResponse> getAdb,
		IUiTransportMessageHandler<UpdateAdbSettingsRequest, UpdateAdbSettingsResponse> updateAdb,
		IUiTransportMessageHandler<RestartAdbServerRequest, RestartAdbServerResponse> restartAdbServer,
		IUiTransportMessageHandler<ConnectAdbDeviceRequest, ConnectAdbDeviceResponse> connectAdbDevice,
		IUiTransportMessageHandler<DownloadAdbPlatformToolsRequest, DownloadAdbPlatformToolsResponse>
			downloadAdbPlatformTools,
		IUiTransportMessageHandler<GetNativeUsbSettingsRequest, GetNativeUsbSettingsResponse> getNativeUsb,
		IUiTransportMessageHandler<UpdateNativeUsbSettingsRequest, UpdateNativeUsbSettingsResponse> updateNativeUsb,
		IUiTransportMessageHandler<RefreshNativeUsbSettingsRequest, GetNativeUsbSettingsResponse> refreshNativeUsb,
		IUiTransportMessageHandler<ConnectNativeUsbDeviceRequest, ConnectNativeUsbDeviceResponse>
			connectNativeUsbDevice,
		IUiTransportMessageHandler<ForgetNativeUsbDeviceRequest, ForgetNativeUsbDeviceResponse>
			forgetNativeUsbDevice,
		IUiTransportMessageHandler<GetDeveloperSettingsRequest, GetDeveloperSettingsResponse> getDeveloper,
		IUiTransportMessageHandler<UpdateDeveloperSettingsRequest, UpdateDeveloperSettingsResponse> updateDeveloper,
		IUiTransportMessageHandler<GetOnboardingStateRequest, GetOnboardingStateResponse> getOnboarding,
		IUiTransportMessageHandler<CompleteOnboardingRequest, CompleteOnboardingResponse> completeOnboarding,
		IUiTransportMessageHandler<GetLockScreenSettingsRequest, GetLockScreenSettingsResponse> getLockScreen,
		IUiTransportMessageHandler<UpdateLockScreenSettingsRequest, UpdateLockScreenSettingsResponse>
			updateLockScreen,
		IUiTransportMessageHandler<GetExtensionSettingsRequest, GetExtensionSettingsResponse> getExtensions,
		IUiTransportMessageHandler<UpdateExtensionSettingsRequest, UpdateExtensionSettingsResponse>
			updateExtensions,
		IUiTransportMessageHandler<GetLocalizationSettingsRequest, GetLocalizationSettingsResponse> getLocalization,
		IUiTransportMessageHandler<UpdateLocalizationSettingsRequest, UpdateLocalizationSettingsResponse>
			updateLocalization)
	{
		_getAppearance = getAppearance;
		_updateAppearance = updateAppearance;
		_getAutostart = getAutostart;
		_updateAutostart = updateAutostart;
		_getHttp = getHttp;
		_updateHttp = updateHttp;
		_getLogging = getLogging;
		_updateLogging = updateLogging;
		_getNetwork = getNetwork;
		_updateNetwork = updateNetwork;
		_updateNetworkTlsCertificate = updateNetworkTlsCertificate;
		_reissueTlsCertificate = reissueTlsCertificate;
		_regenerateTlsCertificateAuthority = regenerateTlsCertificateAuthority;
		_getAdb = getAdb;
		_updateAdb = updateAdb;
		_restartAdbServer = restartAdbServer;
		_connectAdbDevice = connectAdbDevice;
		_downloadAdbPlatformTools = downloadAdbPlatformTools;
		_getNativeUsb = getNativeUsb;
		_updateNativeUsb = updateNativeUsb;
		_refreshNativeUsb = refreshNativeUsb;
		_connectNativeUsbDevice = connectNativeUsbDevice;
		_forgetNativeUsbDevice = forgetNativeUsbDevice;
		_getDeveloper = getDeveloper;
		_updateDeveloper = updateDeveloper;
		_getOnboarding = getOnboarding;
		_completeOnboarding = completeOnboarding;
		_getLockScreen = getLockScreen;
		_updateLockScreen = updateLockScreen;
		_getExtensions = getExtensions;
		_updateExtensions = updateExtensions;
		_getLocalization = getLocalization;
		_updateLocalization = updateLocalization;
	}

	[HttpGet("appearance")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetAppearanceSettingsResponse> GetAppearance(CancellationToken ct)
	{
		return _getAppearance.Handle(new GetAppearanceSettingsRequest(), ct).AsTask();
	}

	[HttpPut("appearance")]
	public Task<UpdateAppearanceSettingsResponse> UpdateAppearance(
		UpdateAppearanceSettingsRequest body,
		CancellationToken ct)
	{
		return _updateAppearance.Handle(body, ct).AsTask();
	}

	[HttpGet("autostart")]
	public Task<GetAutostartSettingsResponse> GetAutostart(CancellationToken ct)
	{
		return _getAutostart.Handle(new GetAutostartSettingsRequest(), ct).AsTask();
	}

	[HttpPut("autostart")]
	public Task<UpdateAutostartSettingsResponse> UpdateAutostart(
		UpdateAutostartSettingsRequest body,
		CancellationToken ct)
	{
		return _updateAutostart.Handle(body, ct).AsTask();
	}

	[HttpGet("network")]
	public Task<GetNetworkSettingsResponse> GetNetwork(CancellationToken ct)
	{
		return _getNetwork.Handle(new GetNetworkSettingsRequest(), ct).AsTask();
	}

	[HttpPut("network")]
	public Task<UpdateNetworkSettingsResponse> UpdateNetwork(
		UpdateNetworkSettingsRequest body,
		CancellationToken ct)
	{
		return _updateNetwork.Handle(body, ct).AsTask();
	}

	[HttpPut("network/tls/certificate")]
	public Task<UpdateNetworkSettingsResponse> UpdateNetworkTlsCertificate(
		UpdateNetworkTlsCertificateRequest body,
		CancellationToken ct)
	{
		return _updateNetworkTlsCertificate.Handle(body, ct).AsTask();
	}

	[HttpPost("network/tls/certificate/reissue")]
	public Task<UpdateNetworkSettingsResponse> ReissueTlsCertificate(CancellationToken ct)
	{
		return _reissueTlsCertificate.Handle(new ReissueTlsCertificateRequest(), ct).AsTask();
	}

	[HttpPost("network/tls/certificate-authority/regenerate")]
	public Task<UpdateNetworkSettingsResponse> RegenerateTlsCertificateAuthority(CancellationToken ct)
	{
		return _regenerateTlsCertificateAuthority.Handle(new RegenerateTlsCertificateAuthorityRequest(), ct).AsTask();
	}

	[HttpGet("http")]
	public Task<GetHttpSettingsResponse> GetHttp(CancellationToken ct)
	{
		return _getHttp.Handle(new GetHttpSettingsRequest(), ct).AsTask();
	}

	[HttpPut("http")]
	public Task<UpdateHttpSettingsResponse> UpdateHttp(UpdateHttpSettingsRequest body, CancellationToken ct)
	{
		return _updateHttp.Handle(body, ct).AsTask();
	}

	[HttpGet("logging")]
	public Task<GetLoggingSettingsResponse> GetLogging(CancellationToken ct)
	{
		return _getLogging.Handle(new GetLoggingSettingsRequest(), ct).AsTask();
	}

	[HttpPut("logging")]
	public Task<UpdateLoggingSettingsResponse> UpdateLogging(
		UpdateLoggingSettingsRequest body,
		CancellationToken ct)
	{
		return _updateLogging.Handle(body, ct).AsTask();
	}

	[HttpGet("adb")]
	public Task<GetAdbSettingsResponse> GetAdb(CancellationToken ct)
	{
		return _getAdb.Handle(new GetAdbSettingsRequest(), ct).AsTask();
	}

	[HttpPut("adb")]
	public Task<UpdateAdbSettingsResponse> UpdateAdb(UpdateAdbSettingsRequest body, CancellationToken ct)
	{
		return _updateAdb.Handle(body, ct).AsTask();
	}

	[HttpPost("adb/restart-server")]
	public Task<RestartAdbServerResponse> RestartAdbServer(CancellationToken ct)
	{
		return _restartAdbServer.Handle(new RestartAdbServerRequest(), ct).AsTask();
	}

	[HttpPost("adb/connect")]
	public Task<ConnectAdbDeviceResponse> ConnectAdbDevice(ConnectAdbDeviceRequest body, CancellationToken ct)
	{
		return _connectAdbDevice.Handle(body, ct).AsTask();
	}

	[HttpPost("adb/download-platform-tools")]
	public Task<DownloadAdbPlatformToolsResponse> DownloadAdbPlatformTools(CancellationToken ct)
	{
		return _downloadAdbPlatformTools.Handle(new DownloadAdbPlatformToolsRequest(), ct).AsTask();
	}

	[HttpGet("usb")]
	public Task<GetNativeUsbSettingsResponse> GetNativeUsb(CancellationToken ct)
	{
		return _getNativeUsb.Handle(new GetNativeUsbSettingsRequest(), ct).AsTask();
	}

	[HttpPut("usb")]
	public Task<UpdateNativeUsbSettingsResponse> UpdateNativeUsb(
		UpdateNativeUsbSettingsRequest body,
		CancellationToken ct)
	{
		return _updateNativeUsb.Handle(body, ct).AsTask();
	}

	[HttpPost("usb/refresh")]
	public Task<GetNativeUsbSettingsResponse> RefreshNativeUsb(CancellationToken ct)
	{
		return _refreshNativeUsb.Handle(new RefreshNativeUsbSettingsRequest(), ct).AsTask();
	}

	[HttpPost("usb/connect")]
	public Task<ConnectNativeUsbDeviceResponse> ConnectNativeUsbDevice(
		ConnectNativeUsbDeviceRequest body,
		CancellationToken ct)
	{
		return _connectNativeUsbDevice.Handle(body, ct).AsTask();
	}

	[HttpPost("usb/forget")]
	public Task<ForgetNativeUsbDeviceResponse> ForgetNativeUsbDevice(
		ForgetNativeUsbDeviceRequest body,
		CancellationToken ct)
	{
		return _forgetNativeUsbDevice.Handle(body, ct).AsTask();
	}

	[HttpGet("developer")]
	public Task<GetDeveloperSettingsResponse> GetDeveloper(CancellationToken ct)
	{
		return _getDeveloper.Handle(new GetDeveloperSettingsRequest(), ct).AsTask();
	}

	[HttpPut("developer")]
	public Task<UpdateDeveloperSettingsResponse> UpdateDeveloper(
		UpdateDeveloperSettingsRequest body,
		CancellationToken ct)
	{
		return _updateDeveloper.Handle(body, ct).AsTask();
	}

	[HttpGet("onboarding")]
	public Task<GetOnboardingStateResponse> GetOnboarding(CancellationToken ct)
	{
		return _getOnboarding.Handle(new GetOnboardingStateRequest(), ct).AsTask();
	}

	[HttpPost("onboarding/complete")]
	public Task<CompleteOnboardingResponse> CompleteOnboarding(CancellationToken ct)
	{
		return _completeOnboarding.Handle(new CompleteOnboardingRequest(), ct).AsTask();
	}

	[HttpGet("lock-screen")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetLockScreenSettingsResponse> GetLockScreen(CancellationToken ct)
	{
		return _getLockScreen.Handle(new GetLockScreenSettingsRequest(), ct).AsTask();
	}

	[HttpPut("lock-screen")]
	public Task<UpdateLockScreenSettingsResponse> UpdateLockScreen(
		UpdateLockScreenSettingsRequest body,
		CancellationToken ct)
	{
		return _updateLockScreen.Handle(body, ct).AsTask();
	}

	[HttpGet("extensions")]
	public Task<GetExtensionSettingsResponse> GetExtensions(CancellationToken ct)
	{
		return _getExtensions.Handle(new GetExtensionSettingsRequest(), ct).AsTask();
	}

	[HttpPut("extensions")]
	public Task<UpdateExtensionSettingsResponse> UpdateExtensions(
		UpdateExtensionSettingsRequest body,
		CancellationToken ct)
	{
		return _updateExtensions.Handle(body, ct).AsTask();
	}

	[HttpGet("localization")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetLocalizationSettingsResponse> GetLocalization(CancellationToken ct)
	{
		return _getLocalization.Handle(new GetLocalizationSettingsRequest(), ct).AsTask();
	}

	[HttpPut("localization")]
	public Task<UpdateLocalizationSettingsResponse> UpdateLocalization(
		UpdateLocalizationSettingsRequest body,
		CancellationToken ct)
	{
		return _updateLocalization.Handle(body, ct).AsTask();
	}
}
