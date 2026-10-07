using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Ui.Handlers;

public class
	UpdateProfileRequestMessageHandler : IUiTransportMessageHandler<UpdateProfileRequest, UpdateProfileResponse>
{
	private readonly IProfileService _profileService;
	private readonly IColorReferenceResolver _colors;
	private readonly IProfileCache _profiles;

	public UpdateProfileRequestMessageHandler(IProfileService profileService,
		IColorReferenceResolver colors,
		IProfileCache profiles)
	{
		_colors = colors;
		_profiles = profiles;
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
			ColorSource.Incoming(request.DefaultBackgroundColorSource,
				request.DefaultBackgroundColor,
				_profiles.GetById(id)?.DefaultBackgroundColor,
				_colors),
			request.DefaultWidgetSpacing,
			request.DefaultWidgetBorderRadius,
			request.DefaultEmptyCellStyle,
			request.DefaultWidgetShadows);

		var response = new UpdateProfileResponse { Success = result.Success };

		if (result.Success)
		{
			response.Profile = ProfileDtoMapper.MapJsonProfile(result.Data!, _colors);
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
