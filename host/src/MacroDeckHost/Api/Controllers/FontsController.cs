using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Fonts;
using MacroDeckHost.Localization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/fonts")]
public class FontsController : ControllerBase
{
	private const long MaxRequestBytes = 128L * 1024 * 1024;

	private readonly IUserFontLibrary _library;

	public FontsController(IUserFontLibrary library)
	{
		_library = library;
	}

	[HttpGet]
	public GetUserFontsResponse GetAll()
		=> new() { Fonts = _library.List().Select(UserFontModel.From).ToList() };

	[HttpPost("import")]
	[DisableFormValueModelBinding]
	[RequestSizeLimit(MaxRequestBytes)]
	[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
	public async Task<ImportUserFontsResponse> Import(CancellationToken ct)
	{
		var files = IconUploadReader.TryReadFiles(Request, ct);
		if (files is null)
		{
			return new ImportUserFontsResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = "ValidationError",
					Message = AppStrings.Errors.Common.MultipartBodyRequired()
				}
			};
		}

		var uploads = new List<UserFontUpload>();
		await foreach (var file in files.WithCancellation(ct))
		{
			using var buffer = new MemoryStream();
			await file.Content.CopyToAsync(buffer, ct);
			uploads.Add(new UserFontUpload(Path.GetFileName(file.FileName), buffer.ToArray()));
		}

		var results = await _library.Import(uploads, ct);
		return new ImportUserFontsResponse
		{
			Success = true,
			Results = results.Select(result => new UserFontImportResultModel
				{
					FileName = result.FileName,
					Status = result.Status,
					Font = result.Font is null ? null : UserFontModel.From(result.Font)
				})
				.ToList()
		};
	}

	[HttpDelete("{fontId}")]
	public async Task<DeleteUserFontResponse> Delete(string fontId, CancellationToken ct)
		=> new() { Success = await _library.Remove(fontId, ct) };
}
