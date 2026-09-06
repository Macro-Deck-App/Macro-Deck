using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Secrets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class CreateSecretRequestMessageHandler
	: IUiTransportMessageHandler<CreateSecretRequest, CreateSecretResponse>
{
	private readonly ISecretService _secretService;

	public CreateSecretRequestMessageHandler(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async ValueTask<CreateSecretResponse> Handle(
		CreateSecretRequest request,
		CancellationToken cancellationToken)
	{
		var id = await _secretService.Create(request.Value, request.Kind);

		return new CreateSecretResponse { Id = id };
	}
}

public class UpdateSecretRequestMessageHandler
	: IUiTransportMessageHandler<UpdateSecretRequest, UpdateSecretResponse>
{
	private readonly ISecretService _secretService;

	public UpdateSecretRequestMessageHandler(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async ValueTask<UpdateSecretResponse> Handle(
		UpdateSecretRequest request,
		CancellationToken cancellationToken)
	{
		var success = await _secretService.Replace(request.Id, request.Value);

		return new UpdateSecretResponse
		{
			Success = success,
			Error = success
				? null
				: new TransportError
				{
					Code = "SECRET_NOT_FOUND", Message = AppStrings.Errors.Secrets.NotFound(id: request.Id.ToString())
				}
		};
	}
}

public class DeleteSecretRequestMessageHandler
	: IUiTransportMessageHandler<DeleteSecretRequest, DeleteSecretResponse>
{
	private readonly ISecretService _secretService;

	public DeleteSecretRequestMessageHandler(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async ValueTask<DeleteSecretResponse> Handle(
		DeleteSecretRequest request,
		CancellationToken cancellationToken)
	{
		var success = await _secretService.Delete(request.Id);

		return new DeleteSecretResponse { Success = success };
	}
}

public class CloneSecretRequestMessageHandler
	: IUiTransportMessageHandler<CloneSecretRequest, CloneSecretResponse>
{
	private readonly ISecretService _secretService;

	public CloneSecretRequestMessageHandler(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async ValueTask<CloneSecretResponse> Handle(
		CloneSecretRequest request,
		CancellationToken cancellationToken)
	{
		var id = await _secretService.Clone(request.Id);

		return new CloneSecretResponse
		{
			Id = id,
			Error = id is null
				? new TransportError
				{
					Code = "SECRET_NOT_FOUND", Message = AppStrings.Errors.Secrets.NotFound(id: request.Id.ToString())
				}
				: null
		};
	}
}

public class RevealSecretRequestMessageHandler
	: IUiTransportMessageHandler<RevealSecretRequest, RevealSecretResponse>
{
	private readonly ISecretService _secretService;

	public RevealSecretRequestMessageHandler(ISecretService secretService)
	{
		_secretService = secretService;
	}

	public async ValueTask<RevealSecretResponse> Handle(
		RevealSecretRequest request,
		CancellationToken cancellationToken)
	{
		var value = await _secretService.Reveal(request.Id);

		return new RevealSecretResponse
		{
			Value = value,
			Error = value is null
				? new TransportError
					{ Code = "SECRET_NOT_REVEALABLE", Message = AppStrings.Errors.Secrets.NotRevealable() }
				: null
		};
	}
}
