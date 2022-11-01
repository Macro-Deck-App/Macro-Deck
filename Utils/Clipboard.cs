using Newtonsoft.Json;

namespace SuchByte.MacroDeck.Utils;

public class Clipboard
{
    static ActionButton.ActionButton _actionButtonSource;

    static JsonSerializerSettings jsonSerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        NullValueHandling = NullValueHandling.Ignore,
            
        Error = (sender, args) => { args.ErrorContext.Handled = true; }
    };

    public static void CopyActionButton(ActionButton.ActionButton actionButton)
    {
        _actionButtonSource = actionButton;
    }
    public static ActionButton.ActionButton GetActionButtonCopy()
    {
        if (_actionButtonSource == null) return null;
        if (_actionButtonSource.IsDisposed) return null;

        // Create a copy of the action button instance
        return JsonConvert.DeserializeObject<ActionButton.ActionButton>(JsonConvert.SerializeObject(_actionButtonSource, jsonSerializerSettings), jsonSerializerSettings);
    }






}