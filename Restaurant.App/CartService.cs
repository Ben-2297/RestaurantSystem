using System.Text.Json;

namespace Restaurant.App;

public static class CartService
{
    private const string CartKey = "UserCartData";
    private const string PendingCheckoutKey = "PendingCheckoutItems";

    public static List<CartItem> GetCart()
    {
        string json = Preferences.Default.Get(CartKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json)) return new List<CartItem>();

        try
        {
            return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
        }
        catch
        {
            return new List<CartItem>();
        }
    }

    public static void SaveCart(List<CartItem> items)
    {
        string json = JsonSerializer.Serialize(items);
        Preferences.Default.Set(CartKey, json);
    }

    public static List<CartItem> GetSelectedItems()
    {
        return GetCart().Where(i => i.IsSelected).ToList();
    }

    public static decimal GetSelectedTotalPrice()
    {
        return GetSelectedItems().Sum(i => i.Subtotal);
    }

    public static void SavePendingCheckoutItems(List<CartItem> items)
    {
        string json = JsonSerializer.Serialize(items);
        Preferences.Default.Set(PendingCheckoutKey, json);
    }

    public static List<CartItem> GetPendingCheckoutItems()
    {
        string json = Preferences.Default.Get(PendingCheckoutKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<CartItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
        }
        catch
        {
            return new List<CartItem>();
        }
    }

    public static void ClearPendingCheckoutItems()
    {
        Preferences.Default.Remove(PendingCheckoutKey);
    }

    public static void RemoveCheckedOutItems(List<CartItem> checkedOutItems)
    {
        var cart = GetCart();

        foreach (var checkedOut in checkedOutItems)
        {
            cart.RemoveAll(i => i.Product.Id == checkedOut.Product.Id && i.Portion == checkedOut.Portion);
        }

        SaveCart(cart);
    }

    public static void AddOrUpdateItem(CartItem newItem)
    {
        var cart = GetCart();

        var existing = cart.FirstOrDefault(i => i.Product.Id == newItem.Product.Id && i.Portion == newItem.Portion);

        if (existing != null)
        {
            existing.Quantity += newItem.Quantity;
        }
        else
        {
            cart.Add(newItem);
        }

        SaveCart(cart);
    }

    public static void ClearCart()
    {
        Preferences.Default.Remove(CartKey);
    }

    public static decimal GetTotalPrice()
    {
        return GetCart().Sum(i => i.Subtotal);
    }
}