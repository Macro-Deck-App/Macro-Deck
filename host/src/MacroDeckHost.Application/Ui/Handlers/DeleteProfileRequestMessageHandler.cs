using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class
	DeleteProfileRequestMessageHandler : IUiTransportMessageHandler<DeleteProfileRequest, DeleteProfileResponse>
{
	private readonly IProfileService _profileService;

	public DeleteProfileRequestMessageHandler(IProfileService profileService)
	{
		_profileService = profileService;
	}

	public async ValueTask<DeleteProfileResponse> Handle(
		DeleteProfileRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.Id, out var id))
		{
			return new DeleteProfileResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = nameof(ProfileError.IsVirtual),
					Message = AppStrings.Errors.Profiles.VirtualCannotBeDeleted()
				}
			};
		}

		var result = await _profileService.Delete(id);

		var response = new DeleteProfileResponse { Success = result.Success };

		if (!result.Success)
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
