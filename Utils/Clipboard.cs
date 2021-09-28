using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace SuchByte.MacroDeck.Utils
{
    public class Clipboard
    {
        static ActionButton.ActionButton _actionButtonCopy = null;


        public static void CopyActionButton(ActionButton.ActionButton actionButton)
        {
            _actionButtonCopy = JsonConvert.DeserializeObject<ActionButton.ActionButton>(JsonConvert.SerializeObject(actionButton, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            }), new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore,
                Error = (sender, args) => { args.ErrorContext.Handled = true; }
            });
        }

        public static ActionButton.ActionButton GetActionButtonCopy()
        {
            return _actionButtonCopy;
        }






    }
}
