namespace Restaurant.App;

public enum PortionType
{
    Full,
    Half
}

public class CartItem
{
    public MenuItem Product { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public PortionType Portion { get; set; } = PortionType.Full;
    public bool IsSelected { get; set; } = true;

    public string PortionText
    {
        get => Portion == PortionType.Half ? "Half" : "Full";
        set
        {
            Portion = string.Equals(value, "Half", StringComparison.OrdinalIgnoreCase)
                ? PortionType.Half
                : PortionType.Full;
        }
    }

    // Half serving calculates at 50% of the full price
    public decimal UnitPrice => Portion == PortionType.Half ? Product.Price * 0.5m : Product.Price;

    public decimal Subtotal => UnitPrice * Quantity;

    public string FormattedSubtotal => $"${Subtotal:F2}";
}