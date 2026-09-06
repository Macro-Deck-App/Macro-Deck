using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/filesystem")]
public class FilesystemController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetFilesystemEntriesRequest, GetFilesystemEntriesResponse> _getEntries;

	public FilesystemController(
		IUiTransportMessageHandler<GetFilesystemEntriesRequest, GetFilesystemEntriesResponse> getEntries)
	{
		_getEntries = getEntries;
	}

	[HttpPost("list")]
	public Task<GetFilesystemEntriesResponse> List(GetFilesystemEntriesRequest body, CancellationToken ct)
		=> _getEntries.Handle(body, ct).AsTask();
}
