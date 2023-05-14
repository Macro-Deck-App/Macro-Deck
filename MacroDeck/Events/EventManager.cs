using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Events;

public static class EventManager
{

    private static List<IMacroDeckEvent> _registeredEvents = new();

    public static List<IMacroDeckEvent> RegisteredEvents => _registeredEvents;

    public static void RegisterEvent(IMacroDeckEvent macroDeckEvent)
    {
        if (!_registeredEvents.Contains(macroDeckEvent))
        {
            _registeredEvents.Add(macroDeckEvent);
            macroDeckEvent.OnEvent += OnActionButtonEventTrigger;
        }
    }

    public static IMacroDeckEvent GetEventByName(string name)
    {
        var macroDeckEvent = _registeredEvents.Find(macroDeckEvent => macroDeckEvent.Name.Equals(name));
        return macroDeckEvent;
    }
    private static void OnActionButtonEventTrigger(object sender, MacroDeckEventArgs e)
    {
        Task.Run(() =>
        {
            var macroDeckEvent = (IMacroDeckEvent)sender;
            var actionButton = e.ActionButton;

            foreach (var action in actionButton.EventListeners.FindAll(x =>
                             x.EventToListen.Equals(macroDeckEvent.Name) &&
                             x.Parameter.ToLower()
                                 .Equals(e.Parameter.ToString()
                                     .ToLower()))
                         .SelectMany(eventListener => eventListener.Actions))
            {
                action.Trigger("-1", e.ActionButton);
            }
        });
    }


}