using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Secrets;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/secrets")]
public class SecretsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<CreateSecretRequest, CreateSecretResponse> _createSecret;
	private readonly IUiTransportMessageHandler<UpdateSecretRequest, UpdateSecretResponse> _updateSecret;
	private readonly IUiTransportMessageHandler<DeleteSecretRequest, DeleteSecretResponse> _deleteSecret;
	private readonly IUiTransportMessageHandler<RevealSecretRequest, RevealSecretResponse> _revealSecret;
	private readonly IUiTransportMessageHandler<CloneSecretRequest, CloneSecretResponse> _cloneSecret;

	public SecretsController(
		IUiTransportMessageHandler<CreateSecretRequest, CreateSecretResponse> createSecret,
		IUiTransportMessageHandler<UpdateSecretRequest, UpdateSecretResponse> updateSecret,
		IUiTransportMessageHandler<DeleteSecretRequest, DeleteSecretResponse> deleteSecret,
		IUiTransportMessageHandler<RevealSecretRequest, RevealSecretResponse> revealSecret,
		IUiTransportMessageHandler<CloneSecretRequest, CloneSecretResponse> cloneSecret)
	{
		_createSecret = createSecret;
		_updateSecret = updateSecret;
		_deleteSecret = deleteSecret;
		_revealSecret = revealSecret;
		_cloneSecret = cloneSecret;
	}

	[HttpPost]
	public Task<CreateSecretResponse> Create(CreateSecretRequest body, CancellationToken ct)
		=> _createSecret.Handle(body, ct).AsTask();

	[HttpPut]
	public Task<UpdateSecretResponse> Update(UpdateSecretRequest body, CancellationToken ct)
		=> _updateSecret.Handle(body, ct).AsTask();

	[HttpDelete("{id:guid}")]
	public Task<DeleteSecretResponse> Delete(Guid id, CancellationToken ct)
		=> _deleteSecret.Handle(new DeleteSecretRequest { Id = id }, ct).AsTask();

	[HttpPost("{id:guid}/reveal")]
	public Task<RevealSecretResponse> Reveal(Guid id, CancellationToken ct)
		=> _revealSecret.Handle(new RevealSecretRequest { Id = id }, ct).AsTask();

	[HttpPost("{id:guid}/clone")]
	public Task<CloneSecretResponse> Clone(Guid id, CancellationToken ct)
		=> _cloneSecret.Handle(new CloneSecretRequest { Id = id }, ct).AsTask();
}
