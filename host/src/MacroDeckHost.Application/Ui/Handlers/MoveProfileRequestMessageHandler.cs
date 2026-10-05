using MacroDeck.Localization;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class MoveProfileRequestMessageHandler : IUiTransportMessageHandler<MoveProfileRequest, MoveProfileResponse>
{
	private readonly IProfileService _profileService;

	public MoveProfileRequestMessageHandler(IProfileService profileService)
	{
		_profileService = profileService;
	}

	public async ValueTask<MoveProfileResponse> Handle(MoveProfileRequest request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(request.Id) || string.IsNullOrEmpty(request.TargetId))
		{
			return Fail(ProfileError.ValidationError, AppStrings.Errors.Profiles.MoveIdsRequired());
		}

		if (!Guid.TryParse(request.Id, out var id) || !Guid.TryParse(request.TargetId, out var targetId))
		{
			return Fail(ProfileError.IsVirtual, AppStrings.Errors.Profiles.VirtualCannotBeEdited());
		}

		ProfileMovePosition position;
		if (string.Equals(request.Position, "before", StringComparison.OrdinalIgnoreCase))
		{
			position = ProfileMovePosition.Before;
		}
		else if (string.Equals(request.Position, "after", StringComparison.OrdinalIgnoreCase))
		{
			position = ProfileMovePosition.After;
		}
		else
		{
			return Fail(ProfileError.ValidationError, AppStrings.Errors.Profiles.InvalidMovePosition());
		}

		var result = await _profileService.Move(id, targetId, position);

		if (!result.Success)
		{
			return Fail(result.Error!.Value, result.ErrorMessage ?? string.Empty);
		}

		return new MoveProfileResponse
		{
			Success = true,
			Profiles = result.Data!.Select(ProfileDtoMapper.MapToPlacement).ToList()
		};
	}

	private static MoveProfileResponse Fail(ProfileError error, LocalizedText message)
		=> new()
		{
			Success = false,
			Error = new TransportError { Code = error.ToString(), Message = message }
		};
}
