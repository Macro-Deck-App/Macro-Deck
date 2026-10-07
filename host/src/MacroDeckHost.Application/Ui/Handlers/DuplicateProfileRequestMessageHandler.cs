using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DuplicateProfileRequestMessageHandler
	: IUiTransportMessageHandler<DuplicateProfileRequest, DuplicateProfileResponse>
{
	private readonly IProfileService _profileService;
	private readonly IColorReferenceResolver _colors;

	public DuplicateProfileRequestMessageHandler(IProfileService profileService, IColorReferenceResolver colors)
	{
		_colors = colors;
		_profileService = profileService;
	}

	public async ValueTask<DuplicateProfileResponse> Handle(
		DuplicateProfileRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DuplicateProfileResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(ProfileError.IsVirtual),
					Message = AppStrings.Errors.Profiles.VirtualCannotBeDuplicated()
				}
			};
		}

		var result = await _profileService.Duplicate(id, request.Name);

		var response = new DuplicateProfileResponse { Success = result.Success };

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
