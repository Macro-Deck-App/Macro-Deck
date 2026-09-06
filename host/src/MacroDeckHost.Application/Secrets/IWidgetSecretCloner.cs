namespace MacroDeckHost.Application.Secrets;

public interface IWidgetSecretCloner
{
	Task<string?> CloneReferencedSecrets(string? widgetData);
}
