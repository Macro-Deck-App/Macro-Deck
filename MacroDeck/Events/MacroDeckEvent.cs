namespace SuchByte.MacroDeck.Events;

public class MacroDeckEventArgs : EventArgs
{
    public ActionButton.ActionButton ActionButton { get; set; }
    public object Parameter { get; set; }
}

public interface IMacroDeckEvent
{
    string Name { get; }
    EventHandler<MacroDeckEventArgs> OnEvent { get; set; }
    List<string> ParameterSuggestions { get; set; }

}