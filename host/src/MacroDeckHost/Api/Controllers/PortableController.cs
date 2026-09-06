using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Ui.Transport.Messages.Portable;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/portable")]
public class PortableController : ControllerBase
{
	private readonly IPortableArchiveInspector _inspector;

	public PortableController(IPortableArchiveInspector inspector)
	{
		_inspector = inspector;
	}

	[HttpPost("inspect")]
	[RequestSizeLimit(PortabilityHttp.MaxRequestBytes)]
	public async Task<InspectArchiveResponse> Inspect([FromForm] IFormFile? file, CancellationToken ct)
	{
		var bytes = await ArchivePathReader.ReadUpload(file, ct);
		if (!bytes.Success)
		{
			return new InspectArchiveResponse
			{
				Success = false,
				Error = PortabilityHttp.ToTransportError(bytes.Error!.Value, bytes.ErrorMessage)
			};
		}

		var result = await _inspector.Inspect(bytes.Data!, ct);
		if (!result.Success)
		{
			return new InspectArchiveResponse
			{
				Success = false,
				Error = PortabilityHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		return InspectArchiveResponse.From(result.Data!);
	}

	[HttpPost("inspect-path")]
	public async Task<InspectArchiveResponse> InspectPath(InspectArchivePathRequest body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new InspectArchiveResponse { Success = false, Error = refusal };
		}

		var bytes = await ArchivePathReader.Read(body.Path,
			PortableFileExtensions.All,
			PortabilityHttp.MaxRequestBytes,
			ct);
		if (!bytes.Success)
		{
			return new InspectArchiveResponse
			{
				Success = false,
				Error = PortabilityHttp.ToTransportError(bytes.Error!.Value, bytes.ErrorMessage)
			};
		}

		var result = await _inspector.Inspect(bytes.Data!, ct);
		if (!result.Success)
		{
			return new InspectArchiveResponse
			{
				Success = false,
				Error = PortabilityHttp.ToTransportError(result.Error!.Value, result.ErrorMessage)
			};
		}

		return InspectArchiveResponse.From(result.Data!);
	}
}
