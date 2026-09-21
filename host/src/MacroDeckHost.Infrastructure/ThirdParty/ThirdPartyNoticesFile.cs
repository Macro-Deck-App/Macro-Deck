using MacroDeckHost.Application.ThirdParty;

namespace MacroDeckHost.Infrastructure.ThirdParty;

public sealed class ThirdPartyNoticesFile : IThirdPartyNotices
{
	public const string FileName = "THIRD-PARTY-NOTICES";

	private readonly string _path;
	private readonly Lazy<string?> _text;
	private readonly Lazy<ThirdPartyNoticesDocument?> _document;

	public ThirdPartyNoticesFile()
		: this(Path.Combine(AppContext.BaseDirectory, FileName))
	{
	}

	internal ThirdPartyNoticesFile(string path)
	{
		_path = path;
		_text = new Lazy<string?>(() => File.Exists(_path) ? File.ReadAllText(_path) : null);
		_document = new Lazy<ThirdPartyNoticesDocument?>(() => _text.Value is { } text ? ThirdPartyNoticesDocument.Parse(text) : null);
	}

	public string? ReadText() => _text.Value;

	public ThirdPartyNoticesDocument? Read() => _document.Value;
}
