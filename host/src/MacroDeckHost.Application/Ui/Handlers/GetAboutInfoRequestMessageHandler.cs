using System.Runtime.InteropServices;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetAboutInfoRequestMessageHandler : IUiTransportMessageHandler<GetAboutInfoRequest, GetAboutInfoResponse>
{
	internal const string License = "Apache-2.0";

	public ValueTask<GetAboutInfoResponse> Handle(GetAboutInfoRequest request, CancellationToken cancellationToken)
	{
		var response = new GetAboutInfoResponse
		{
			Version = HostVersion.Current,
			IsBeta = HostVersion.IsBeta,
			IsDevelopmentBuild = BuildConfig.Channel == BuildChannel.Development,
			Commit = HostBuildInfo.Commit,
			BuildTimestamp = HostBuildInfo.BuildTimestamp,
			BuildNumber = HostBuildInfo.BuildNumber,
			License = License,
			RuntimeVersion = RuntimeInformation.FrameworkDescription,
			OperatingSystem = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})"
		};

		return ValueTask.FromResult(response);
	}
}
