using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.API.Data;
using Restaurant.API.Services;
using System.Security.Claims;

namespace Restaurant.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CateringChatController : ControllerBase
{
    private readonly IGoogleChatService _googleChatService;
    private readonly DataContext _context;

    public CateringChatController(IGoogleChatService googleChatService, DataContext context)
    {
        _googleChatService = googleChatService;
        _context = context;
    }

    [HttpPost("messages")]
    public async Task<ActionResult<CateringChatSendResponse>> SendMessage(
        [FromBody] CateringChatSendRequest request,
        CancellationToken cancellationToken)
    {
        string text = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("Message is required.");
        }

        if (text.Length > 2000)
        {
            return BadRequest("Message is too long. Maximum 2000 characters.");
        }

        string username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown User";
        string role = User.FindFirstValue(ClaimTypes.Role) ?? "User";

        string fullName = username;
        var user = await _context.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (!string.IsNullOrWhiteSpace(user?.Profile?.FullName))
        {
            fullName = user.Profile.FullName;
        }

        string outgoingMessage =
            "Fiesta Catering Inquiry\n" +
            $"From: {fullName} ({username})\n" +
            $"Role: {role}\n" +
            $"Sent (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n\n" +
            text;

        await _googleChatService.SendAdminMessageAsync(outgoingMessage, cancellationToken);

        return Ok(new CateringChatSendResponse
        {
            Delivered = true,
            TimestampUtc = DateTime.UtcNow,
            Echo = text
        });
    }
}

public class CateringChatSendRequest
{
    public string Message { get; set; } = string.Empty;
}

public class CateringChatSendResponse
{
    public bool Delivered { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Echo { get; set; } = string.Empty;
}