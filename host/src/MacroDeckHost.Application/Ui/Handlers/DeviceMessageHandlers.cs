using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetDevicesRequestMessageHandler : IUiTransportMessageHandler<GetDevicesRequest, GetDevicesResponse>
{
	private readonly IDeviceService _service;
	private readonly StartupReadiness _readiness;

	public GetDevicesRequestMessageHandler(IDeviceService service, StartupReadiness readiness)
	{
		_service = service;
		_readiness = readiness;
	}

	public async ValueTask<GetDevicesResponse> Handle(GetDevicesRequest request, CancellationToken cancellationToken)
	{
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		return new GetDevicesResponse { Devices = (await _service.GetAll()).ToList() };
	}
}

public class RenameDeviceRequestMessageHandler : IUiTransportMessageHandler<RenameDeviceRequest, RenameDeviceResponse>
{
	private readonly IDeviceService _service;

	public RenameDeviceRequestMessageHandler(IDeviceService service)
	{
		_service = service;
	}

	public async ValueTask<RenameDeviceResponse> Handle(
		RenameDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _service.Rename(request.Id, request.Name);
		if (!result.Success || result.Data is null)
		{
			return new RenameDeviceResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = result.Error!.Value.ToString(), Message = result.ErrorMessage ?? string.Empty }
			};
		}

		return new RenameDeviceResponse { Success = true, Device = await _service.ToDto(result.Data) };
	}
}

public class SetDeviceStartupProfileRequestMessageHandler
	: IUiTransportMessageHandler<SetDeviceStartupProfileRequest, SetDeviceStartupProfileResponse>
{
	private readonly IDeviceService _service;

	public SetDeviceStartupProfileRequestMessageHandler(IDeviceService service)
	{
		_service = service;
	}

	public async ValueTask<SetDeviceStartupProfileResponse> Handle(
		SetDeviceStartupProfileRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _service.SetStartupProfile(request.Id, request.ProfileId);
		if (!result.Success || result.Data is null)
		{
			return new SetDeviceStartupProfileResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = result.Error!.Value.ToString(), Message = result.ErrorMessage ?? string.Empty }
			};
		}

		return new SetDeviceStartupProfileResponse { Success = true, Device = await _service.ToDto(result.Data) };
	}
}

public class LogoutDeviceRequestMessageHandler : IUiTransportMessageHandler<LogoutDeviceRequest, LogoutDeviceResponse>
{
	private readonly IDeviceService _service;

	public LogoutDeviceRequestMessageHandler(IDeviceService service)
	{
		_service = service;
	}

	public async ValueTask<LogoutDeviceResponse> Handle(
		LogoutDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _service.LogoutDevice(request.Id);
		if (!result.Success)
		{
			return new LogoutDeviceResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = result.Error!.Value.ToString(), Message = result.ErrorMessage ?? string.Empty }
			};
		}

		return new LogoutDeviceResponse { Success = true };
	}
}

public class RemoveDeviceRequestMessageHandler : IUiTransportMessageHandler<RemoveDeviceRequest, RemoveDeviceResponse>
{
	private readonly IDeviceService _service;

	public RemoveDeviceRequestMessageHandler(IDeviceService service)
	{
		_service = service;
	}

	public async ValueTask<RemoveDeviceResponse> Handle(
		RemoveDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _service.RemoveDevice(request.Id);
		if (!result.Success)
		{
			return new RemoveDeviceResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = result.Error!.Value.ToString(), Message = result.ErrorMessage ?? string.Empty }
			};
		}

		return new RemoveDeviceResponse { Success = true };
	}
}

public class OpenProfileOnDeviceRequestMessageHandler
	: IUiTransportMessageHandler<OpenProfileOnDeviceRequest, OpenProfileOnDeviceResponse>
{
	private readonly IDeviceService _service;

	public OpenProfileOnDeviceRequestMessageHandler(IDeviceService service)
	{
		_service = service;
	}

	public async ValueTask<OpenProfileOnDeviceResponse> Handle(
		OpenProfileOnDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _service.OpenProfileOnDevice(request.Id, request.ProfileId);
		if (!result.Success)
		{
			return new OpenProfileOnDeviceResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = result.Error!.Value.ToString(), Message = result.ErrorMessage ?? string.Empty }
			};
		}

		return new OpenProfileOnDeviceResponse { Success = true };
	}
}
