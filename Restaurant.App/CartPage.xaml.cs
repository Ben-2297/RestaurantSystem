namespace Restaurant.App;

public partial class CartPage : ContentPage
{
    private List<CartItem> cartItems = new();
    private bool isUpdatingSelectAllState;

    public CartPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadCartItems();
    }

    private void LoadCartItems()
    {
        cartItems = CartService.GetCart();
        CartItemsCollectionView.ItemsSource = cartItems;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        int selectedCount = cartItems.Count(i => i.IsSelected);
        bool hasItems = cartItems.Count > 0;
        bool allSelected = hasItems && selectedCount == cartItems.Count;
        decimal selectedSubtotal = cartItems.Where(i => i.IsSelected).Sum(i => i.Subtotal);

        isUpdatingSelectAllState = true;
        SelectAllCheckBox.IsChecked = allSelected;
        SelectAllCheckBox.IsEnabled = hasItems;
        isUpdatingSelectAllState = false;

        SelectedSummaryLabel.Text = hasItems
            ? $"{selectedCount} of {cartItems.Count} selected"
            : "0 selected";
        SelectedSubtotalLabel.Text = $"${selectedSubtotal:F2} selected";

        TotalPriceLabel.Text = $"${selectedSubtotal:F2}";
    }

    private void OnSelectAllChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (isUpdatingSelectAllState || cartItems.Count == 0)
        {
            return;
        }

        foreach (var item in cartItems)
        {
            item.IsSelected = e.Value;
        }

        CartService.SaveCart(cartItems);
        LoadCartItems();
    }

    private void OnItemSelectionChanged(object? sender, CheckedChangedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.BindingContext is CartItem item)
        {
            item.IsSelected = e.Value;
            CartService.SaveCart(cartItems);
            LoadCartItems();
        }
    }

    private void OnPortionChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker && picker.BindingContext is CartItem item && picker.SelectedItem is string selectedPortion)
        {
            item.PortionText = selectedPortion;
            ConsolidateCartItems();
            CartService.SaveCart(cartItems);
            LoadCartItems();
        }
    }

    private void OnDecreaseQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is CartItem item)
        {
            if (item.Quantity > 1)
            {
                item.Quantity -= 1;
                CartService.SaveCart(cartItems);
                LoadCartItems();
            }
        }
    }

    private void OnIncreaseQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is CartItem item)
        {
            item.Quantity += 1;
            CartService.SaveCart(cartItems);
            LoadCartItems();
        }
    }

    private void OnRemoveItemClicked(object? sender, EventArgs e)
    {
        if (sender is Button button && button.CommandParameter is CartItem itemToRemove)
        {
            cartItems.RemoveAll(i => i.Product.Id == itemToRemove.Product.Id && i.Portion == itemToRemove.Portion);
            CartService.SaveCart(cartItems);
            LoadCartItems();
        }
    }

    private async void OnCheckoutClicked(object? sender, EventArgs e)
    {
        var selectedItems = CartService.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            await this.DisplayAlertAsync("No Items Selected", "Select at least one cart item to proceed to checkout.", "OK");
            return;
        }

        CartService.SavePendingCheckoutItems(selectedItems);

        await Navigation.PushAsync(new CheckoutPage());
    }

    private void ConsolidateCartItems()
    {
        cartItems = cartItems
            .GroupBy(i => new { ProductId = i.Product.Id, i.Portion })
            .Select(g => new CartItem
            {
                Product = g.First().Product,
                Portion = g.Key.Portion,
                Quantity = g.Sum(x => x.Quantity),
                IsSelected = g.Any(x => x.IsSelected)
            })
            .ToList();
    }
}