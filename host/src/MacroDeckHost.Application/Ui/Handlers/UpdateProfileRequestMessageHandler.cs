using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class
	UpdateProfileRequestMessageHandler : IUiTransportMessageHandler<UpdateProfileRequest, UpdateProfileResponse>
{
	private readonly IProfileService _profileService;

	public UpdateProfileRequestMessageHandler(IProfileService profileService)
	{
		_profileService = profileService;
	}

	public async ValueTask<UpdateProfileResponse> Handle(
		UpdateProfileRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new UpdateProfileResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(ProfileError.IsVirtual),
					Message = AppStrings.Errors.Profiles.VirtualCannotBeEdited()
				}
			};
		}

		var result = await _profileService.Update(id,
			request.Name,
			request.Order,
			request.DefaultRows,
			request.DefaultColumns,
			request.DefaultBackgroundColor,
			request.DefaultWidgetSpacing,
			request.DefaultWidgetBorderRadius);

		var response = new UpdateProfileResponse { Success = result.Success };

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
