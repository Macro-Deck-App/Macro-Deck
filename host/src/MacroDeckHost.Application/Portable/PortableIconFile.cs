namespace MacroDeckHost.Application.Portable;

public sealed record PortableIconFile(Guid IconId, string Variant, byte[] Bytes);
