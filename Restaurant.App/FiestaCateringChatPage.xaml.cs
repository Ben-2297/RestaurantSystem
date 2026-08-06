using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Restaurant.App;

public partial class FiestaCateringChatPage : ContentPage
{
    private static readonly HttpClient HttpClient = new HttpClient();

    public ObservableCollection<FiestaChatMessage> Messages { get; } = new();

    public FiestaCateringChatPage()
    {
        InitializeComponent();
        BindingContext = this;

        Messages.Add(new FiestaChatMessage
        {
            Sender = "Admin Channel",
            Text = "Welcome! Send your catering details and our admin team will receive them in Google Chat.",
            TimeStamp = DateTime.Now.ToString("hh:mm tt"),
            Alignment = LayoutOptions.Start,
            BubbleColor = Color.FromArgb("#f3f4f6")
        });
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        string rawText = MessageEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            await this.DisplayAlertAsync("Validation", "Please enter a message.", "OK");
            return;
        }

        var outgoing = new FiestaChatMessage
        {
            Sender = "You",
            Text = rawText,
            TimeStamp = DateTime.Now.ToString("hh:mm tt"),
            Alignment = LayoutOptions.End,
            BubbleColor = Color.FromArgb("#dcfce7")
        };

        Messages.Add(outgoing);
        MessageEditor.Text = string.Empty;
        await ScrollToLatestAsync();

        try
        {
            ApiAuthHelper.ApplyAuthHeader(HttpClient);

            var payload = new
            {
                message = rawText
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await HttpClient.PostAsync(
                $"{ApiSettings.BaseUrl}/api/cateringchat/messages",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Messages.Add(new FiestaChatMessage
                {
                    Sender = "System",
                    Text = $"Delivery failed: {errorBody}",
                    TimeStamp = DateTime.Now.ToString("hh:mm tt"),
                    Alignment = LayoutOptions.Start,
                    BubbleColor = Color.FromArgb("#fee2e2")
                });

                await ScrollToLatestAsync();
                return;
            }

            Messages.Add(new FiestaChatMessage
            {
                Sender = "System",
                Text = "Delivered to admin Google Chat channel.",
                TimeStamp = DateTime.Now.ToString("hh:mm tt"),
                Alignment = LayoutOptions.Start,
                BubbleColor = Color.FromArgb("#e0f2fe")
            });

            await ScrollToLatestAsync();
        }
        catch (Exception ex)
        {
            Messages.Add(new FiestaChatMessage
            {
                Sender = "System",
                Text = $"Connection error: {ex.Message}",
                TimeStamp = DateTime.Now.ToString("hh:mm tt"),
                Alignment = LayoutOptions.Start,
                BubbleColor = Color.FromArgb("#fee2e2")
            });

            await ScrollToLatestAsync();
        }
    }

    private Task ScrollToLatestAsync()
    {
        if (Messages.Count > 0)
        {
            ChatCollectionView.ScrollTo(Messages[^1], position: ScrollToPosition.End, animate: true);
        }

        return Task.CompletedTask;
    }
}

public class FiestaChatMessage
{
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string TimeStamp { get; set; } = string.Empty;
    public LayoutOptions Alignment { get; set; } = LayoutOptions.Start;
    public Color BubbleColor { get; set; } = Colors.White;
}