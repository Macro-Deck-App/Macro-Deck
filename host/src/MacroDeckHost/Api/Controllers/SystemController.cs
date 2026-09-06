using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using MacroDeckHost.Application.Ui.Transport.Messages.Version;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetVersionRequest, GetVersionResponse> _getVersion;
	private readonly IUiTransportMessageHandler<GetAboutInfoRequest, GetAboutInfoResponse> _getAboutInfo;
	private readonly IUiTransportMessageHandler<GetSystemFontsRequest, GetSystemFontsResponse> _getSystemFonts;

	private readonly IUiTransportMessageHandler<GetServerTimeRequest, GetServerTimeResponse> _getServerTime;

	private readonly IUiTransportMessageHandler<GetApplicationFocusCapabilityRequest,
			GetApplicationFocusCapabilityResponse>
		_getApplicationFocusCapability;

	private readonly IUiTransportMessageHandler<GetRunningApplicationsRequest, GetRunningApplicationsResponse>
		_getRunningApplications;

	private readonly IUiTransportMessageHandler<GetLockStateRequest, GetLockStateResponse> _getLockState;

	private readonly IFontCatalog _fontCatalog;
	private readonly IHostListenerState _listenerState;

	public SystemController(
		IUiTransportMessageHandler<GetVersionRequest, GetVersionResponse> getVersion,
		IUiTransportMessageHandler<GetAboutInfoRequest, GetAboutInfoResponse> getAboutInfo,
		IUiTransportMessageHandler<GetSystemFontsRequest, GetSystemFontsResponse> getSystemFonts,
		IUiTransportMessageHandler<GetServerTimeRequest, GetServerTimeResponse> getServerTime,
		IUiTransportMessageHandler<GetApplicationFocusCapabilityRequest, GetApplicationFocusCapabilityResponse>
			getApplicationFocusCapability,
		IUiTransportMessageHandler<GetRunningApplicationsRequest, GetRunningApplicationsResponse>
			getRunningApplications,
		IUiTransportMessageHandler<GetLockStateRequest, GetLockStateResponse> getLockState,
		IFontCatalog fontCatalog,
		IHostListenerState listenerState)
	{
		_getVersion = getVersion;
		_getAboutInfo = getAboutInfo;
		_getSystemFonts = getSystemFonts;
		_getServerTime = getServerTime;
		_getApplicationFocusCapability = getApplicationFocusCapability;
		_getRunningApplications = getRunningApplications;
		_getLockState = getLockState;
		_fontCatalog = fontCatalog;
		_listenerState = listenerState;
	}

	[HttpGet("version")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetVersionResponse> GetVersion(CancellationToken ct)
		=> _getVersion.Handle(new GetVersionRequest(), ct).AsTask();

	[HttpGet("about")]
	public Task<GetAboutInfoResponse> GetAbout(CancellationToken ct)
		=> _getAboutInfo.Handle(new GetAboutInfoRequest(), ct).AsTask();

	[HttpGet("time")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public async Task<GetServerTimeResponse> GetServerTime(CancellationToken ct)
	{
		Response.Headers.CacheControl = "no-store";
		return await _getServerTime.Handle(new GetServerTimeRequest(), ct);
	}

	[HttpGet("fonts")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetSystemFontsResponse> GetFonts(CancellationToken ct)
		=> _getSystemFonts.Handle(new GetSystemFontsRequest(), ct).AsTask();

	[HttpGet("build-info")]
	[AllowAnonymous]
	public GetBuildInfoResponse GetBuildInfo()
	{
		Response.Headers.CacheControl = "no-store";
		return new GetBuildInfoResponse { Commit = HostBuildInfo.Commit };
	}

	[HttpGet("connection-info")]
	public GetConnectionInfoResponse GetConnectionInfo()
	{
		var endpoints = GetReachableIpv4Addresses()
			.SelectMany(address => _listenerState.PublicEndpoints.Endpoints,
				(address, endpoint) => new ConnectionEndpointDto
				{
					Address = address,
					Port = endpoint.Port,
					Ssl = endpoint.Ssl
				})
			.ToList();

		return new GetConnectionInfoResponse
		{
			InstanceName = Environment.MachineName,
			Endpoints = endpoints,
			PublicListenerUnavailable = !_listenerState.PublicListenerAvailable,
			Version = HostVersion.Current
		};
	}

	private static List<string> GetReachableIpv4Addresses()
	{
		return NetworkInterface.GetAllNetworkInterfaces()
			.Where(nic =>
				nic.OperationalStatus == OperationalStatus.Up &&
				nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
			.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
				!IPAddress.IsLoopback(address.Address))
			.Select(address => address.Address.ToString())
			.Distinct()
			.ToList();
	}

	[HttpGet("application-focus")]
	public Task<GetApplicationFocusCapabilityResponse> GetApplicationFocusCapability(CancellationToken ct)
		=> _getApplicationFocusCapability.Handle(new GetApplicationFocusCapabilityRequest(), ct).AsTask();

	[HttpGet("running-applications")]
	public Task<GetRunningApplicationsResponse> GetRunningApplications([FromQuery] string? filter,
		CancellationToken ct)
		=> _getRunningApplications.Handle(new GetRunningApplicationsRequest { Filter = filter }, ct).AsTask();

	[HttpGet("lock-state")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetLockStateResponse> GetLockState(CancellationToken ct)
		=> _getLockState.Handle(new GetLockStateRequest(), ct).AsTask();

	[HttpGet("fonts/{faceId}/file")]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public IActionResult GetFontFile(string? faceId)
	{
		var bytes = faceId is null ? null : _fontCatalog.GetFaceFile(faceId);
		if (bytes is null)
		{
			return NotFound();
		}

		Response.Headers.CacheControl = "public, max-age=31536000, immutable";
		return File(bytes, SfntContentType(bytes), lastModified: null, new EntityTagHeaderValue($"\"{faceId}\""));
	}

	private static string SfntContentType(byte[] sfnt)
		=> sfnt.Length >= 4 &&
			sfnt[0] == (byte)'O' &&
			sfnt[1] == (byte)'T' &&
			sfnt[2] == (byte)'T' &&
			sfnt[3] == (byte)'O'
				? "font/otf"
				: "font/ttf";
}
