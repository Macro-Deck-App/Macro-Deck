using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace SuchByte.MacroDeck.Events
{
    public static class EventManager
    {

        private static List<IMacroDeckEvent> _registeredEvents = new List<IMacroDeckEvent>();

        public static List<IMacroDeckEvent> RegisteredEvents { get { return _registeredEvents; } }

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
            IMacroDeckEvent macroDeckEvent = _registeredEvents.Find(macroDeckEvent => macroDeckEvent.Name.Equals(name));
            return macroDeckEvent;
        }
        private static void OnActionButtonEventTrigger(object sender, MacroDeckEventArgs e)
        {
            Task.Run(() =>
            {
                IMacroDeckEvent macroDeckEvent = (IMacroDeckEvent)sender;
                ActionButton.ActionButton actionButton = e.ActionButton;
                Debug.WriteLine(sender.ToString());
                Debug.WriteLine(e.Parameter);

                foreach (EventListener eventListener in actionButton.EventListeners.FindAll(x => x.EventToListen.Equals(macroDeckEvent.Name) && x.Parameter.ToLower().Equals(e.Parameter.ToString().ToLower())))
                {
                    foreach (PluginAction action in eventListener.Actions)
                    {
                        action.Trigger("-1", e.ActionButton);
                    }
                }
            });
        }


    }
}
