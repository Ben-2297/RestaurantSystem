using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;

namespace Restaurant.App;

public partial class CheckoutPage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private const string PendingCheckoutSessionIdKey = "PendingCheckoutSessionId";
    private List<CartItem> checkoutItems = new();

    private int _pendingOrderId;
    private string _pendingCheckoutSessionId = string.Empty;
    private bool _didOpenStripeCheckout;

    public CheckoutPage()
    {
        InitializeComponent();
        _pendingOrderId = Preferences.Default.Get("PendingOrderId", 0);
        _pendingCheckoutSessionId = Preferences.Default.Get(PendingCheckoutSessionIdKey, string.Empty);
        LoadOrderPreview();
        PrefillCustomerProfile();

        if (_pendingOrderId > 0)
        {
            OrderStatusLabel.Text = $"Status: Checking order #{_pendingOrderId}...";
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var returnedSessionId = Preferences.Default.Get(App.CheckoutReturnSessionIdKey, string.Empty);
        var returnedStatus = Preferences.Default.Get(App.CheckoutReturnStatusKey, string.Empty);
        var cameBackFromStripe = _didOpenStripeCheckout || string.Equals(returnedStatus, "checkout-success", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(returnedSessionId))
        {
            _pendingCheckoutSessionId = returnedSessionId;
            Preferences.Default.Set(PendingCheckoutSessionIdKey, _pendingCheckoutSessionId);
            Preferences.Default.Remove(App.CheckoutReturnSessionIdKey);
        }

        if (!string.IsNullOrWhiteSpace(returnedStatus))
        {
            Preferences.Default.Remove(App.CheckoutReturnStatusKey);
        }

        if (_pendingOrderId <= 0)
        {
            _pendingOrderId = Preferences.Default.Get("PendingOrderId", 0);
        }

        if (_pendingOrderId > 0)
        {
            if (!string.IsNullOrWhiteSpace(_pendingCheckoutSessionId))
            {
                await VerifyCheckoutSessionAsync(_pendingCheckoutSessionId);
            }

            await RefreshPendingOrderStateAsync(showPaidAlert: cameBackFromStripe, closeCheckoutOnPaid: cameBackFromStripe);
            _didOpenStripeCheckout = false;
        }
    }

    private void LoadOrderPreview()
    {
        checkoutItems = CartService.GetPendingCheckoutItems();
        if (checkoutItems.Count == 0)
        {
            checkoutItems = CartService.GetSelectedItems();
        }

        if (checkoutItems.Count == 0)
        {
            checkoutItems = CartService.GetCart();
        }

        OrderSummaryCollectionView.ItemsSource = checkoutItems;
    }

    private void PrefillCustomerProfile()
    {
        FullNameEntry.Text = Preferences.Default.Get("UserFullName", string.Empty);
        EmailEntry.Text = Preferences.Default.Get("UserEmail", string.Empty);
        PhoneEntry.Text = Preferences.Default.Get("UserPhone", string.Empty);
    }

    private async void OnSubmitOrderClicked(object? sender, EventArgs e)
    {
        if (checkoutItems.Count == 0)
        {
            await DisplayAlertAsync("Cart Empty", "Your cart is empty.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(FullNameEntry.Text) || string.IsNullOrWhiteSpace(EmailEntry.Text))
        {
            await DisplayAlertAsync("Validation", "Please fill in your name and email.", "OK");
            return;
        }

        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);

            var payload = new
            {
                userId = Preferences.Default.Get("UserId", 0),
                customerName = FullNameEntry.Text?.Trim(),
                customerEmail = EmailEntry.Text?.Trim(),
                phoneNumber = PhoneEntry.Text?.Trim(),
                pickupNotes = PickupNoteEditor.Text?.Trim() ?? string.Empty,
                items = checkoutItems.Select(i => new
                {
                    productId = i.Product.Id,
                    quantity = i.Quantity,
                    isHalfOption = i.Portion == PortionType.Half,
                    unitPrice = i.UnitPrice
                }).ToList()
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync($"{ApiSettings.BaseUrl}/api/orders/checkout", content);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                await DisplayAlertAsync("Order Error", responseText, "OK");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            _pendingOrderId = doc.RootElement.GetProperty("orderId").GetInt32();
            Preferences.Default.Set("PendingOrderId", _pendingOrderId);
            var status = doc.RootElement.GetProperty("status").GetString() ?? "Order";
            OrderStatusLabel.Text = $"Status: Order submitted successfully. Admin review status: {status}";

            PayNowButton.IsEnabled = false;
            RecheckPaymentButton.IsVisible = true;
            await DisplayAlertAsync("Order Submitted", "The order is now waiting for admin confirmation. Once confirmed, you can pay securely using Stripe.", "OK");
                await RefreshPendingOrderStateAsync(showPaidAlert: false, closeCheckoutOnPaid: false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Connection Failure", ex.Message, "OK");
        }
    }

    private async void OnPayNowClicked(object? sender, EventArgs e)
    {
        if (_pendingOrderId <= 0)
        {
            await DisplayAlertAsync("Order Not Ready", "Please submit the order first.", "OK");
            return;
        }

        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);

            var payload = new
            {
                orderId = _pendingOrderId
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync($"{ApiSettings.BaseUrl}/api/payments/create-checkout-session", content);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                await DisplayAlertAsync("Payment Error", responseText, "OK");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var checkoutUrl = doc.RootElement.GetProperty("checkoutUrl").GetString();
            _pendingCheckoutSessionId = doc.RootElement.TryGetProperty("sessionId", out var sessionElement)
                ? sessionElement.GetString() ?? string.Empty
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(_pendingCheckoutSessionId))
            {
                Preferences.Default.Set(PendingCheckoutSessionIdKey, _pendingCheckoutSessionId);
            }

            if (string.IsNullOrWhiteSpace(checkoutUrl))
            {
                await DisplayAlertAsync("Payment Error", "Stripe checkout URL was not returned by the server.", "OK");
                return;
            }

            OrderStatusLabel.Text = "Status: Stripe checkout opened. Complete payment in the browser and return to the app.";
            _didOpenStripeCheckout = true;
            await Browser.Default.OpenAsync(checkoutUrl, BrowserLaunchMode.SystemPreferred);
            await DisplayAlertAsync("Continue in Browser", "You were redirected to Stripe Checkout. After paying, return to the app to check your updated payment status.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Payment Failure", ex.Message, "OK");
        }
    }

    private async void OnRecheckPaymentClicked(object? sender, EventArgs e)
    {
        if (_pendingOrderId <= 0)
        {
            _pendingOrderId = Preferences.Default.Get("PendingOrderId", 0);
        }

        if (_pendingOrderId <= 0)
        {
            await DisplayAlertAsync("No Pending Order", "There is no pending order to refresh.", "OK");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingCheckoutSessionId))
        {
            await VerifyCheckoutSessionAsync(_pendingCheckoutSessionId);
        }

        await RefreshPendingOrderStateAsync(showPaidAlert: true, closeCheckoutOnPaid: false);
    }

    private async Task VerifyCheckoutSessionAsync(string sessionId)
    {
        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);

            var payload = new
            {
                orderId = _pendingOrderId,
                sessionId
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await HttpClient.PostAsync($"{ApiSettings.BaseUrl}/api/payments/verify-checkout-session", content);
        }
        catch
        {
            // If verification cannot be reached, the normal order history check still runs.
        }
    }

    private async Task RefreshPendingOrderStateAsync(bool showPaidAlert, bool closeCheckoutOnPaid)
    {
        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);

            var response = await HttpClient.GetAsync($"{ApiSettings.BaseUrl}/api/orders/history");
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            JsonElement? matchedOrder = null;
            foreach (var order in doc.RootElement.EnumerateArray())
            {
                if (order.TryGetProperty("id", out var idElement) && idElement.GetInt32() == _pendingOrderId)
                {
                    matchedOrder = order;
                    break;
                }
            }

            if (!matchedOrder.HasValue)
            {
                return;
            }

            var orderElement = matchedOrder.Value;
            var status = orderElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString() ?? "Order"
                : "Order";
            var isPaid = orderElement.TryGetProperty("isPaid", out var isPaidElement) && isPaidElement.GetBoolean();

            if (isPaid)
            {
                OrderStatusLabel.Text = "Status: Payment confirmed. Your order is paid.";
                PayNowButton.IsEnabled = false;
                RecheckPaymentButton.IsVisible = false;
                _pendingOrderId = 0;
                _pendingCheckoutSessionId = string.Empty;
                Preferences.Default.Remove("PendingOrderId");
                Preferences.Default.Remove(PendingCheckoutSessionIdKey);

                if (CartService.GetCart().Count > 0)
                {
                    CartService.RemoveCheckedOutItems(checkoutItems);
                    CartService.ClearPendingCheckoutItems();
                    LoadOrderPreview();
                }

                if (showPaidAlert)
                {
                    await DisplayAlertAsync("Payment Completed", "Payment was confirmed by Stripe and your order is now marked as paid.", "OK");
                }

                if (closeCheckoutOnPaid && Navigation.NavigationStack.Count > 1)
                {
                    await Navigation.PopAsync();
                }

                return;
            }

            if (status == "Confirm" || status == "Pick-up")
            {
                OrderStatusLabel.Text = $"Status: Order approved ({status}). You can proceed to Stripe payment.";
                PayNowButton.IsEnabled = true;
                RecheckPaymentButton.IsVisible = true;
            }
            else
            {
                OrderStatusLabel.Text = $"Status: Waiting for admin confirmation ({status}).";
                PayNowButton.IsEnabled = false;
                RecheckPaymentButton.IsVisible = true;
            }
        }
        catch
        {
            // Keep current UI state if refresh fails.
        }
    }
}
