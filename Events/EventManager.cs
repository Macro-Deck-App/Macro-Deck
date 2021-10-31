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
    public class EventManager
    {

        private List<IMacroDeckEvent> _registeredEvents = new List<IMacroDeckEvent>();

        public List<IMacroDeckEvent> RegisteredEvents { get { return this._registeredEvents; } }

        public void RegisterEvent(IMacroDeckEvent macroDeckEvent)
        {
            if (!this._registeredEvents.Contains(macroDeckEvent))
            {
                _registeredEvents.Add(macroDeckEvent);
            }
        }



        public IMacroDeckEvent GetEventByName(string name)
        {
            IMacroDeckEvent macroDeckEvent = this._registeredEvents.Find(macroDeckEvent => macroDeckEvent.Name.Equals(name));
            return macroDeckEvent;
        }

        

    }
}
