using System.Text.Json;

namespace Restaurant.App;

public partial class OrderHistoryPage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public OrderHistoryPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadOrdersAsync();
    }

    private async Task LoadOrdersAsync()
    {
        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);
            var response = await HttpClient.GetAsync($"{ApiSettings.BaseUrl}/api/orders/history");
            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync("Unable to Load Orders", await response.Content.ReadAsStringAsync(), "OK");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<OrderHistoryItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            OrdersCollectionView.ItemsSource = items;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Connection Issue", ex.Message, "OK");
        }
    }

    public class OrderHistoryItem
    {
        public int OrderId { get; set; }
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
