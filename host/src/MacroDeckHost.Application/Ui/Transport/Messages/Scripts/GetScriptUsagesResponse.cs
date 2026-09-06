namespace MacroDeckHost.Application.Ui.Transport.Messages.Scripts;

public class GetScriptUsagesResponse
{
	public List<ScriptUsage> Usages { get; set; } = [];
}

public class ScriptUsage
{
	public string Kind { get; set; } = string.Empty;

	public string Location { get; set; } = string.Empty;

	public int Count { get; set; }
}
