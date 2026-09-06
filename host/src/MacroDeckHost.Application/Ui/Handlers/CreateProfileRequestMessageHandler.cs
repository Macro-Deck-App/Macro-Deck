using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

namespace MacroDeckHost.Application.Ui.Handlers;

public class
	CreateProfileRequestMessageHandler : IUiTransportMessageHandler<CreateProfileRequest, CreateProfileResponse>
{
	private readonly IProfileService _profileService;

	public CreateProfileRequestMessageHandler(IProfileService profileService)
	{
		_profileService = profileService;
	}

	public async ValueTask<CreateProfileResponse> Handle(
		CreateProfileRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _profileService.Create(request.Name,
			layoutType: null,
			request.DefaultRows,
			request.DefaultColumns,
			request.DefaultBackgroundColor,
			request.DefaultWidgetSpacing,
			request.DefaultWidgetBorderRadius);

		var response = new CreateProfileResponse { Success = result.Success };

		if (result.Success)
		{
			response.Profile = ProfileDtoMapper.MapJsonProfile(result.Data!);
		}
		else
		{
			response.Error = new TransportError
			{
				Code = result.Error.ToString()!,
				Message = result.ErrorMessage ?? string.Empty
			};
		}

		return response;
	}
}
