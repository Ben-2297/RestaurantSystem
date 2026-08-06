namespace Restaurant.App;

public class CategoryGroup
{
    public string CategoryName { get; set; } = string.Empty;
    public List<MenuItem> Items { get; set; } = new();

    public bool HasItems => Items != null && Items.Count > 0;
    public bool IsEmpty => !HasItems;
}