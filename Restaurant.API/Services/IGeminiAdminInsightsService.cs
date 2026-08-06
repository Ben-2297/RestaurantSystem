namespace Restaurant.API.Services;

public interface IGeminiAdminInsightsService
{
    Task<string> AskAsync(string adminSessionKey, string prompt, CancellationToken cancellationToken = default);
    Task<AdminInsightsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task ResetSessionAsync(string adminSessionKey, CancellationToken cancellationToken = default);
}

public sealed class AdminInsightsSnapshot
{
    public decimal TotalSales { get; set; }
    public decimal PaidSales { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetProfit { get; set; }
    public int OrderCount { get; set; }
    public List<TopMenuItemSummary> TopMenu { get; set; } = new();
}

public sealed class TopMenuItemSummary
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
}
