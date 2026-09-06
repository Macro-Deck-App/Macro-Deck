using MacroDeck.Sdk.Ui;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

public interface IBuiltInWidgetUiProvider : IUiProvider
{
	/// <summary>The widget type this provider draws - see <see cref="MacroDeckHost.Domain.Widgets.WidgetTypeIds" />.</summary>
	string WidgetTypeId { get; }
}
