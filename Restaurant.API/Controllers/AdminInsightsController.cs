using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant.API.Services;
using System.Security.Claims;

namespace Restaurant.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminInsightsController : ControllerBase
{
    private readonly IGeminiAdminInsightsService _geminiAdminInsightsService;

    public AdminInsightsController(IGeminiAdminInsightsService geminiAdminInsightsService)
    {
        _geminiAdminInsightsService = geminiAdminInsightsService;
    }

    [HttpPost("chat")]
    public async Task<ActionResult<AdminInsightsChatResponse>> Chat(
        [FromBody] AdminInsightsChatRequest request,
        CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest("Message is required.");
        }

        if (message.Length > 4000)
        {
            return BadRequest("Message is too long. Maximum 4000 characters.");
        }

        var adminSessionKey = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "admin";

        var reply = await _geminiAdminInsightsService.AskAsync(adminSessionKey, message, cancellationToken);
        var snapshot = await _geminiAdminInsightsService.GetSnapshotAsync(cancellationToken);

        return Ok(new AdminInsightsChatResponse
        {
            Reply = reply,
            TimestampUtc = DateTime.UtcNow,
            Snapshot = snapshot
        });
    }

    [HttpPost("chat/reset")]
    public async Task<IActionResult> ResetChat(CancellationToken cancellationToken)
    {
        var adminSessionKey = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? "admin";

        await _geminiAdminInsightsService.ResetSessionAsync(adminSessionKey, cancellationToken);
        return NoContent();
    }
}

public class AdminInsightsChatRequest
{
    public string Message { get; set; } = string.Empty;
}

public class AdminInsightsChatResponse
{
    public string Reply { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public AdminInsightsSnapshot? Snapshot { get; set; }
}
