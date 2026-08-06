namespace Restaurant.API.Services;

public interface IGoogleChatService
{
    Task SendAdminMessageAsync(string text, CancellationToken cancellationToken = default);
}