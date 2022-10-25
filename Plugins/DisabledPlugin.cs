namespace SuchByte.MacroDeck.Plugins
{
    public class DisabledPlugin : MacroDeckPlugin
    {
        internal override string Name { get; set; }
        internal override string Version { get; set; }
        internal override string Author { get; set; }
        public override void Enable() {}

    }
}
