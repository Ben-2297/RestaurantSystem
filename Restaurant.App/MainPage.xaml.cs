using System.Text.Json;
using MenuItem = Restaurant.App.MenuItem;

namespace Restaurant.App;

public partial class MainPage : ContentPage
{
    public static bool IsUserLoggedIn { get; set; } = false;

    private static readonly HttpClient HttpClient = new HttpClient();

    private readonly List<string> DefinedCategories = new()
    {
        "Pork",
        "Noodles",
        "Chicken",
        "Seafoods",
        "Beef",
        "Vegetables",
        "Native Desserts",
        "Beverages"
    };

    public MainPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadCategorizedMenuAsync();
    }

    private async Task LoadCategorizedMenuAsync()
    {
        if (MenuLoadingIndicator != null)
        {
            MenuLoadingIndicator.IsRunning = true;
            MenuLoadingIndicator.IsVisible = true;
        }

        List<MenuItem> allProducts = new();

        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);
            HttpResponseMessage response = await HttpClient.GetAsync($"{ApiSettings.BaseUrl}/api/products");
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                allProducts = JsonSerializer.Deserialize<List<MenuItem>>(json, options) ?? new List<MenuItem>();
            }
        }
        catch
        {
            // API offline fallback
        }

        var groupedCategories = new List<CategoryGroup>();
        var availableProducts = allProducts.Where(p => p.IsAvailable).ToList();

        foreach (var categoryName in DefinedCategories)
        {
            var categoryItems = availableProducts
                .Where(p => string.Equals(p.Category, categoryName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            groupedCategories.Add(new CategoryGroup
            {
                CategoryName = categoryName,
                Items = categoryItems
            });
        }

        if (CategoriesStackLayout != null)
        {
            BindableLayout.SetItemsSource(CategoriesStackLayout, groupedCategories);
        }

        if (MenuLoadingIndicator != null)
        {
            MenuLoadingIndicator.IsRunning = false;
            MenuLoadingIndicator.IsVisible = false;
        }
    }

    private async void OnAddToCartClicked(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        if (sender is Button button && button.CommandParameter is MenuItem selectedItem)
        {
            await Navigation.PushAsync(new AddToCartPage(selectedItem));
        }
    }

    private async void OnLocationTapped(object? sender, EventArgs? e)
    {
        try
        {
            Uri locationUri = new Uri("https://maps.app.goo.gl/cgGGzgiJZSX7Heab9");
            await Launcher.Default.OpenAsync(locationUri);
        }
        catch (Exception ex)
        {
            await this.DisplayAlertAsync("Error", $"Unable to open Google Maps: {ex.Message}", "OK");
        }
    }

    private async void OnProfileHeaderIconTapped(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        await Navigation.PushAsync(new ProfilePage());
    }

    private async void OnHomeTabTapped(object? sender, EventArgs? e)
    {
        if (Navigation.NavigationStack.LastOrDefault() is not MainPage)
        {
            await Navigation.PushAsync(new MainPage());
        }
    }

    private async void OnOrdersTabTapped(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        await Navigation.PushAsync(new OrderHistoryPage());
    }

    private async void OnPaymentTabTapped(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        await Navigation.PushAsync(new PaymentHistoryPage());
    }

    private async void OnCartTabTapped(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        await Navigation.PushAsync(new CartPage());
    }

    private async void OnFiestaCateringTapped(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        await Navigation.PushAsync(new FiestaCateringChatPage());
    }

    private async void OnPickupTapped(object? sender, EventArgs? e)
    {
        bool isAuthenticated = await CheckAuthAndRedirectAsync();
        if (!isAuthenticated) return;

        await Navigation.PushAsync(new OrderHistoryPage());
    }

    private async Task<bool> CheckAuthAndRedirectAsync()
    {
        bool isLoggedIn = Preferences.Default.Get("IsLoggedIn", false);

        if (!isLoggedIn)
        {
            await this.DisplayAlertAsync("Authentication Required", "Please sign in to access this feature.", "OK");
            await Navigation.PushModalAsync(new NavigationPage(new LoginPage()));
            return false;
        }

        return true; 
    }
}