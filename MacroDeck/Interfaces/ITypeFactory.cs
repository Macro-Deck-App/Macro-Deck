namespace SuchByte.MacroDeck.Interfaces;

public interface ITypeFactory<T>
{
    bool TryGet(string param, out T? type);
}