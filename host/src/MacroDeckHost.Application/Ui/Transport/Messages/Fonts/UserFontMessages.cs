using System.Text.Json.Serialization;
using MacroDeckHost.Application.Rendering;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Fonts;

public class UserFontModel
{
	public string FontId { get; set; } = string.Empty;
	public string FaceId { get; set; } = string.Empty;
	public string Family { get; set; } = string.Empty;
	public string StyleName { get; set; } = string.Empty;
	public int Weight { get; set; }
	public int Width { get; set; }
	public string Slant { get; set; } = string.Empty;
	public string Format { get; set; } = string.Empty;
	public long SizeBytes { get; set; }

	public static UserFontModel From(UserFont font) => new()
	{
		FontId = font.FontId,
		FaceId = font.FaceId,
		Family = font.Family,
		StyleName = font.StyleName,
		Weight = font.Weight,
		Width = font.Width,
		Slant = font.Slant,
		Format = font.Format,
		SizeBytes = font.SizeBytes
	};
}

public class GetUserFontsResponse
{
	public List<UserFontModel> Fonts { get; set; } = [];
}

public class UserFontImportResultModel
{
	public string FileName { get; set; } = string.Empty;
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public UserFontImportStatus Status { get; set; }
	public UserFontModel? Font { get; set; }
}

public class ImportUserFontsResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public List<UserFontImportResultModel> Results { get; set; } = [];
}

public class DeleteUserFontResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
}
