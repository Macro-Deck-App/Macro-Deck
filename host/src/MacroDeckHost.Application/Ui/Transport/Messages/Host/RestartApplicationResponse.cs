namespace MacroDeckHost.Application.Ui.Transport.Messages.Host;

public class RestartApplicationResponse
{
	public bool Success { get; set; }

	public bool Supported { get; set; }

	public string? Error { get; set; }
}
