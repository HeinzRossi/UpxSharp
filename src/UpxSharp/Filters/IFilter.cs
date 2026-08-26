namespace UpxSharp.Filters;

public interface IFilter
{
    byte Id { get; }
    void Filter(FilterState state);
    void Unfilter(FilterState state);
}
