using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetApplicationFocusCapabilityRequestMessageHandler
	: IUiTransportMessageHandler<GetApplicationFocusCapabilityRequest, GetApplicationFocusCapabilityResponse>
{
	private readonly IApplicationFocusWatcher _watcher;

	public GetApplicationFocusCapabilityRequestMessageHandler(IApplicationFocusWatcher watcher)
	{
		_watcher = watcher;
	}

	public ValueTask<GetApplicationFocusCapabilityResponse> Handle(GetApplicationFocusCapabilityRequest request,
		CancellationToken cancellationToken)
	{
		var response = new GetApplicationFocusCapabilityResponse
		{
			Supported = _watcher.IsSupported,
			UnsupportedReason = _watcher.UnsupportedReason,
			PreferredIdentityKind = OperatingSystem.IsMacOS()
				? ApplicationIdentityKind.BundleId
				: ApplicationIdentityKind.ExecutablePath
		};

		return ValueTask.FromResult(response);
	}
}
