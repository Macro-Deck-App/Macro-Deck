using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Variables.Colors;

namespace MacroDeckHost.Application.Ui.Handlers;

public class
	CreateProfileRequestMessageHandler : IUiTransportMessageHandler<CreateProfileRequest, CreateProfileResponse>
{
	private readonly IProfileService _profileService;
	private readonly IColorReferenceResolver _colors;
	private readonly StartupReadiness _readiness;

	public CreateProfileRequestMessageHandler(
		IProfileService profileService,
		IColorReferenceResolver colors,
		StartupReadiness readiness)
	{
		_colors = colors;
		_profileService = profileService;
		_readiness = readiness;
	}

	public async ValueTask<CreateProfileResponse> Handle(
		CreateProfileRequest request,
		CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; a profile created before the caches load would
		// read as an existing install and stop the first start from creating the Default Profile.
		await _readiness.WhenCachesReady.WaitAsync(cancellationToken);

		var result = await _profileService.Create(request.Name,
			null,
			request.DefaultRows,
			request.DefaultColumns,
			request.DefaultBackgroundColor,
			request.DefaultWidgetSpacing,
			request.DefaultWidgetBorderRadius,
			request.DefaultEmptyCellStyle,
			request.DefaultWidgetShadows);

		var response = new CreateProfileResponse { Success = result.Success };

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
