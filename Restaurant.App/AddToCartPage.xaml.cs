namespace Restaurant.App;

public partial class AddToCartPage : ContentPage
{
    private readonly MenuItem _selectedProduct;
    private int _quantity = 1;

    public AddToCartPage(MenuItem product)
    {
        InitializeComponent();
        _selectedProduct = product;

        ProductNameLabel.Text = _selectedProduct.Name;
        ProductPriceLabel.Text = $"${_selectedProduct.Price:F2}";
        ProductImage.Source = _selectedProduct.ImageUrl;
    }

    private void OnIncreaseQuantityClicked(object? sender, EventArgs? e)
    {
        _quantity++;
        QuantityLabel.Text = _quantity.ToString();
    }

    private void OnDecreaseQuantityClicked(object? sender, EventArgs? e)
    {
        if (_quantity > 1)
        {
            _quantity--;
            QuantityLabel.Text = _quantity.ToString();
        }
    }

    private async void OnConfirmAddToCartClicked(object? sender, EventArgs? e)
    {
        PortionType selectedPortion = HalfPortionRadio.IsChecked ? PortionType.Half : PortionType.Full;

        var cartItem = new CartItem
        {
            Product = _selectedProduct,
            Quantity = _quantity,
            Portion = selectedPortion
        };

        // Save into local device storage
        CartService.AddOrUpdateItem(cartItem);

        // Fixed compiler warning CS0618 by using DisplayAlertAsync
        await this.DisplayAlertAsync("Added to Cart", $"{_selectedProduct.Name} has been saved to your local cart.", "OK");

        // Return back to MainPage
        await Navigation.PopAsync();
    }
}