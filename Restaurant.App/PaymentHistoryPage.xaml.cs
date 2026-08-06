using System.Text.Json;

namespace Restaurant.App;

public partial class PaymentHistoryPage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public PaymentHistoryPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPaymentsAsync();
    }

    private async Task LoadPaymentsAsync()
    {
        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);
            var response = await HttpClient.GetAsync($"{ApiSettings.BaseUrl}/api/payments/history");
            if (!response.IsSuccessStatusCode)
            {
                await DisplayAlertAsync("Unable to Load Payments", await response.Content.ReadAsStringAsync(), "OK");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<PaymentHistoryItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            PaymentsCollectionView.ItemsSource = items;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Connection Issue", ex.Message, "OK");
        }
    }

    public class PaymentHistoryItem
    {
        public int OrderId { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
