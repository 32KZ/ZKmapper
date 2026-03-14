namespace ZKMapper.Models;

internal sealed class MappingQueue
{
    private readonly List<CompanyInput> _companies = new();

    public IReadOnlyList<CompanyInput> Companies => _companies;

    public int Count => _companies.Count;

    public void Add(CompanyInput input)
    {
        _companies.Add(input);
    }
}
