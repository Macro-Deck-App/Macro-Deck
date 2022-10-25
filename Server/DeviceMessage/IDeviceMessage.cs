using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.Model
{
    public interface IDeviceMessage
    {

        public void Connected(MacroDeckClient macroDeckClient);
        public void SendConfiguration(MacroDeckClient macroDeckClient);
        public void SendAllButtons(MacroDeckClient macroDeckClient);
        public void UpdateButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton);
        


    }
}
