using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Profiles;
using System;
using System.Collections.Generic;
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
            }
        }



        public static IMacroDeckEvent GetEventByName(string name)
        {
            IMacroDeckEvent macroDeckEvent = _registeredEvents.Find(macroDeckEvent => macroDeckEvent.Name.Equals(name));
            return macroDeckEvent;
        }

        

    }
}
