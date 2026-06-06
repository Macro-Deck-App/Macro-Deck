namespace SuchByte.MacroDeck.Models;

public class PagedList<T>
{
    public List<T>? Items { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
}
