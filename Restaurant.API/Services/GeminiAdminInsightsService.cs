using System.ComponentModel;
using System.Globalization;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.EntityFrameworkCore;
using Restaurant.API.Data;
using Restaurant.API.Models;
using System.Text.Json;
using GenAiType = Google.GenAI.Types.Type;

namespace Restaurant.API.Services;

public class GeminiAdminInsightsService : IGeminiAdminInsightsService
{
    private const string ModelName = "gemini-2.0-flash";
    private const int MaxConversationItems = 24;
    private const string MissingApiKeyMessage = "Gemini assistant is not configured on the server yet. Set GEMINI_API_KEY or Gemini:ApiKey in API configuration to enable chat.";
    private readonly DataContext _context;
    private readonly ILogger<GeminiAdminInsightsService> _logger;
    private readonly Client? _client;
    private readonly GenerateContentConfig? _generateConfig;
    private readonly bool _isGeminiEnabled;

    public GeminiAdminInsightsService(
        DataContext context,
        IConfiguration configuration,
        ILogger<GeminiAdminInsightsService> logger)
    {
        _context = context;
        _logger = logger;

        string apiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? configuration["Gemini:ApiKey"]
            ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _isGeminiEnabled = false;
            _logger.LogWarning("Gemini admin insights is disabled because no API key was configured.");
            return;
        }

        _isGeminiEnabled = true;
        _client = new Client(apiKey: apiKey);
        _generateConfig = new GenerateContentConfig
        {
            SystemInstruction = new Content
            {
                Parts =
                [
                    new Part
                    {
                        Text = "You are Gemini, an operations analyst for a restaurant admin dashboard. " +
                               "Use local function tools for restaurant-specific metrics such as sales, orders, expenses, profit, and top menu performance from this system's database. " +
                               "Use Google Search or Google Maps only when the user asks for external information that is not stored in the local restaurant system, such as nearby places, city-level market context, or general web information. " +
                               "Do not invent local data that was not returned by the function tools. " +
                               "Return concise business insights with clear numbers and bullets. " +
                               "If data is unavailable, explain exactly what is missing."
                    }
                ]
            },
            Tools =
            [
                BuildToolDeclaration(),
                new Tool
                {
                    GoogleSearch = new GoogleSearch()
                },
                new Tool
                {
                    GoogleMaps = new GoogleMaps()
                }
            ],
            ToolConfig = new ToolConfig
            {
                FunctionCallingConfig = new FunctionCallingConfig
                {
                    Mode = FunctionCallingConfigMode.Auto
                }
            }
        };
    }

    public async Task<string> AskAsync(string adminSessionKey, string prompt, CancellationToken cancellationToken = default)
    {
        if (!_isGeminiEnabled || _client is null || _generateConfig is null)
        {
            return await BuildFallbackReplyAsync(prompt, includeUnavailableNote: true, cancellationToken);
        }
        try
        {
            var sessionKey = BuildSessionKey(adminSessionKey);
            var conversation = await GetSessionConversationAsync(sessionKey, cancellationToken);

            conversation.Add(new Content
            {
                Role = "user",
                Parts = [new Part { Text = prompt }]
            });

            for (var i = 0; i < 6; i++)
            {
                var response = await ExecuteWithRetryAsync(
                    operation: () => _client.Models.GenerateContentAsync(
                        model: ModelName,
                        contents: conversation,
                        config: _generateConfig,
                        cancellationToken: cancellationToken),
                    operationName: "GenerateContent",
                    cancellationToken: cancellationToken);

                if (response.FunctionCalls is null || response.FunctionCalls.Count == 0)
                {
                    await PersistSessionConversationAsync(sessionKey, conversation, cancellationToken);
                    var finalText = response.Text?.Trim();
                    return string.IsNullOrWhiteSpace(finalText)
                        ? "I could not produce an answer from the available data. Please try rephrasing your question."
                        : finalText;
                }

                var modelParts = response.Parts?.ToList()
                    ?? response.FunctionCalls
                        .Select(call => new Part { FunctionCall = call })
                        .ToList();

                conversation.Add(new Content
                {
                    Role = "model",
                    Parts = modelParts
                });

                var functionResponseParts = new List<Part>();
                foreach (var call in response.FunctionCalls)
                {
                    var output = await ExecuteFunctionCallAsync(call);
                    functionResponseParts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Id = call.Id,
                            Name = call.Name,
                            Response = new Dictionary<string, object>
                            {
                                ["output"] = output
                            }
                        }
                    });
                }

                conversation.Add(new Content
                {
                    Role = "user",
                    Parts = functionResponseParts
                });

                conversation = TrimConversation(conversation, MaxConversationItems);
            }

            await PersistSessionConversationAsync(sessionKey, conversation, cancellationToken);
            return "I ran the calculations but could not complete the response in time. Please try again.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini admin insights request failed.");
            return await BuildFallbackReplyAsync(prompt, includeUnavailableNote: false, cancellationToken);
        }
    }

    public async Task<AdminInsightsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var totalSales = await _context.Orders.AsNoTracking().SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;
        var paidSales = await _context.Orders.AsNoTracking().Where(o => o.IsPaid).SumAsync(o => (decimal?)o.TotalAmount, cancellationToken) ?? 0m;
        var orderCount = await _context.Orders.AsNoTracking().CountAsync(cancellationToken);
        var totalExpense = await _context.Inventory
            .AsNoTracking()
            .SumAsync(i => (decimal?)i.UnitPrice * (decimal)i.StockAmount, cancellationToken) ?? 0m;

        var topMenu = await _context.OrderItems
            .AsNoTracking()
            .GroupBy(i => i.ProductName)
            .Select(g => new TopMenuItemSummary
            {
                ProductName = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.UnitPrice * x.Quantity)
            })
            .OrderByDescending(x => x.Quantity)
            .ThenByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);

        return new AdminInsightsSnapshot
        {
            TotalSales = totalSales,
            PaidSales = paidSales,
            OrderCount = orderCount,
            TotalExpense = totalExpense,
            NetProfit = totalSales - totalExpense,
            TopMenu = topMenu
        };
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientGeminiError(ex))
            {
                var delay = TimeSpan.FromMilliseconds(300 * attempt);
                _logger.LogWarning(ex,
                    "Transient Gemini error during {Operation}. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs}ms.",
                    operationName,
                    attempt,
                    maxAttempts,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Retry execution reached an unexpected state.");
    }

    private static bool IsTransientGeminiError(Exception ex)
    {
        if (ex is HttpRequestException || ex is TimeoutException)
        {
            return true;
        }

        if (ex is TaskCanceledException taskCanceledException &&
            !taskCanceledException.CancellationToken.IsCancellationRequested)
        {
            return true;
        }

        return ex.InnerException is not null && IsTransientGeminiError(ex.InnerException);
    }

    private async Task<string> BuildFallbackReplyAsync(
        string prompt,
        bool includeUnavailableNote,
        CancellationToken cancellationToken)
    {
        var normalizedPrompt = prompt.Trim().ToLowerInvariant();
        var location = ExtractLocationHint(prompt);
        var note = "This answer is generated from local dashboard data.\n\n";

        var snapshot = await GetSnapshotAsync(cancellationToken);

        if (normalizedPrompt.Contains("total sale") || normalizedPrompt.Contains("sales") || normalizedPrompt.Contains("revenue"))
        {
            return note + await GetSalesSummaryAsync(null, null, location);
        }

        if (normalizedPrompt.Contains("profit"))
        {
            return note + await GetProfitSummaryAsync(null, null, location);
        }

        if (normalizedPrompt.Contains("expense") || normalizedPrompt.Contains("cost"))
        {
            return note + $"Total inventory expense: {snapshot.TotalExpense:C2}";
        }

        if (normalizedPrompt.Contains("top menu") || normalizedPrompt.Contains("best seller") || normalizedPrompt.Contains("top item"))
        {
            if (location is not null)
            {
                return note + await GetTopMenuSummaryAsync(5, null, null, location);
            }

            if (snapshot.TopMenu.Count == 0)
            {
                return note + "No top-selling menu items are available yet because there are no recorded order items.";
            }

            var rows = snapshot.TopMenu
                .Take(5)
                .Select((item, index) => $"{index + 1}. {item.ProductName} - Qty {item.Quantity}, Revenue {item.Revenue:C2}");

            return note + "Top menu items:\n" + string.Join("\n", rows);
        }

        return note +
            $"Current dashboard summary:\n" +
            $"Total sales: {snapshot.TotalSales:C2}\n" +
            $"Paid sales: {snapshot.PaidSales:C2}\n" +
            $"Total expense: {snapshot.TotalExpense:C2}\n" +
            $"Net profit: {snapshot.NetProfit:C2}\n" +
            $"Orders: {snapshot.OrderCount}";
    }
    public Task ResetSessionAsync(string adminSessionKey, CancellationToken cancellationToken = default)
    {
        return ResetSessionInternalAsync(BuildSessionKey(adminSessionKey), cancellationToken);
    }

    private async Task<List<Content>> GetSessionConversationAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var rows = await _context.AdminInsightsChatHistory
            .AsNoTracking()
            .Where(x => x.SessionKey == sessionKey)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxConversationItems)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new List<Content>();
        }

        rows.Reverse();

        var conversation = new List<Content>(rows.Count);
        foreach (var row in rows)
        {
            try
            {
                var content = JsonSerializer.Deserialize<Content>(row.PayloadJson);
                if (content is not null)
                {
                    conversation.Add(content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping invalid chat history row {RowId} for session {SessionKey}.", row.Id, sessionKey);
            }
        }

        return conversation;
    }

    private async Task PersistSessionConversationAsync(string sessionKey, List<Content> conversation, CancellationToken cancellationToken)
    {
        await ResetSessionInternalAsync(sessionKey, cancellationToken);

        var trimmed = TrimConversation(conversation, MaxConversationItems);
        var nowUtc = DateTime.UtcNow;

        var entities = trimmed.Select((content, index) => new AdminInsightsChatEntry
        {
            SessionKey = sessionKey,
            Role = content.Role ?? string.Empty,
            PayloadJson = JsonSerializer.Serialize(content),
            CreatedAtUtc = nowUtc.AddMilliseconds(index)
        });

        await _context.AdminInsightsChatHistory.AddRangeAsync(entities, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static List<Content> TrimConversation(List<Content> conversation, int maxItems)
    {
        if (conversation.Count <= maxItems)
        {
            return conversation;
        }

        return conversation.Skip(conversation.Count - maxItems).ToList();
    }

    private static string BuildSessionKey(string adminSessionKey)
    {
        return $"admin:{adminSessionKey}";
    }

    private async Task ResetSessionInternalAsync(string sessionKey, CancellationToken cancellationToken)
    {
        var rows = await _context.AdminInsightsChatHistory
            .Where(x => x.SessionKey == sessionKey)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        _context.AdminInsightsChatHistory.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ExecuteFunctionCallAsync(FunctionCall call)
    {
        if (call.Name is null)
        {
            return "Invalid function call: missing function name.";
        }

        try
        {
            switch (call.Name)
            {
                case "get_sales":
                    return await GetSalesSummaryAsync(
                        GetStringArg(call.Args, "fromDate"),
                        GetStringArg(call.Args, "toDate"),
                        GetStringArg(call.Args, "location"));
                case "get_expenses":
                    return await GetExpensesSummaryAsync(
                        GetStringArg(call.Args, "fromDate"),
                        GetStringArg(call.Args, "toDate"));
                case "get_profit":
                    return await GetProfitSummaryAsync(
                        GetStringArg(call.Args, "fromDate"),
                        GetStringArg(call.Args, "toDate"),
                        GetStringArg(call.Args, "location"));
                case "get_top_menu":
                    return await GetTopMenuSummaryAsync(
                        GetIntArg(call.Args, "topN") ?? 5,
                        GetStringArg(call.Args, "fromDate"),
                        GetStringArg(call.Args, "toDate"),
                        GetStringArg(call.Args, "location"));
                default:
                    return $"Unsupported function '{call.Name}'.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Function call {FunctionName} failed.", call.Name);
            return $"Function {call.Name} failed: {ex.Message}";
        }
    }

    private static Tool BuildToolDeclaration()
    {
        return new Tool
        {
            FunctionDeclarations =
            [
                new FunctionDeclaration
                {
                    Name = "get_sales",
                    Description = "Get total sales and paid sales from orders in an optional date range and optional delivery-address location filter.",
                    Parameters = BuildDateWindowSchema(includeLocation: true)
                },
                new FunctionDeclaration
                {
                    Name = "get_expenses",
                    Description = "Get current total expense from inventory stock and unit prices.",
                    Parameters = BuildDateWindowSchema()
                },
                new FunctionDeclaration
                {
                    Name = "get_profit",
                    Description = "Get net profit using total sales minus total expense, with optional date range and delivery-address location filter for sales.",
                    Parameters = BuildDateWindowSchema(includeLocation: true)
                },
                new FunctionDeclaration
                {
                    Name = "get_top_menu",
                    Description = "Get top menu items by quantity sold and revenue in an optional date range.",
                    Parameters = new Schema
                    {
                        Type = GenAiType.Object,
                        Properties = new Dictionary<string, Schema>
                        {
                            ["topN"] = new Schema
                            {
                                Type = GenAiType.Integer,
                                Description = "Number of menu items to return. Use 1 to 20."
                            },
                            ["fromDate"] = new Schema
                            {
                                Type = GenAiType.String,
                                Description = "Optional start date in yyyy-MM-dd format."
                            },
                            ["toDate"] = new Schema
                            {
                                Type = GenAiType.String,
                                Description = "Optional end date in yyyy-MM-dd format."
                            },
                            ["location"] = new Schema
                            {
                                Type = GenAiType.String,
                                Description = "Optional location or city name to match against the order delivery address, for example New York or Brooklyn."
                            }
                        }
                    }
                }
            ]
        };
    }

    private static Schema BuildDateWindowSchema(bool includeLocation = false)
    {
        var properties = new Dictionary<string, Schema>
        {
            ["fromDate"] = new Schema
            {
                Type = GenAiType.String,
                Description = "Optional start date in yyyy-MM-dd format."
            },
            ["toDate"] = new Schema
            {
                Type = GenAiType.String,
                Description = "Optional end date in yyyy-MM-dd format."
            }
        };

        if (includeLocation)
        {
            properties["location"] = new Schema
            {
                Type = GenAiType.String,
                Description = "Optional location or city name to match against the order delivery address, for example New York or Manhattan."
            };
        }

        return new Schema
        {
            Type = GenAiType.Object,
            Properties = properties
        };
    }

    private static string? GetStringArg(IReadOnlyDictionary<string, object>? args, string key)
    {
        if (args is null || !args.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string s => s,
            _ => value.ToString()
        };
    }

    private static int? GetIntArg(IReadOnlyDictionary<string, object>? args, string key)
    {
        if (args is null || !args.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            double d when d is >= int.MinValue and <= int.MaxValue => (int)d,
            float f when f is >= int.MinValue and <= int.MaxValue => (int)f,
            decimal m when m is >= int.MinValue and <= int.MaxValue => (int)m,
            _ when int.TryParse(value.ToString(), out var parsed) => parsed,
            _ => null
        };
    }

    private async Task<string> GetSalesSummaryAsync(
        [Description("Optional start date in yyyy-MM-dd format")] string? fromDate,
        [Description("Optional end date in yyyy-MM-dd format")] string? toDate,
        [Description("Optional location or city name to match against delivery address")] string? location)
    {
        var (startUtc, endUtc) = ParseDateWindow(fromDate, toDate);

        var query = ApplyLocationFilter(_context.Orders.AsNoTracking().AsQueryable(), location);
        if (startUtc.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            query = query.Where(o => o.CreatedAt < endUtc.Value);
        }

        var totalSales = await query.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var orderCount = await query.CountAsync();
        var paidSales = await query.Where(o => o.IsPaid).SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var locationLabel = BuildLocationLabel(location);

        return $"Sales summary{locationLabel}: totalSales={totalSales:F2}, paidSales={paidSales:F2}, orderCount={orderCount}.";
    }

    private async Task<string> GetExpensesSummaryAsync(
        [Description("Optional start date in yyyy-MM-dd format. Currently informational only because inventory costs are current-state totals.")] string? fromDate,
        [Description("Optional end date in yyyy-MM-dd format. Currently informational only because inventory costs are current-state totals.")] string? toDate)
    {
        _ = ParseDateWindow(fromDate, toDate);

        var inventoryCost = await _context.Inventory
            .AsNoTracking()
            .SumAsync(i => (decimal?)i.UnitPrice * (decimal)i.StockAmount) ?? 0m;

        var inventoryCount = await _context.Inventory.AsNoTracking().CountAsync();

        return $"Expense summary: inventoryExpense={inventoryCost:F2}, inventoryItemCount={inventoryCount}.";
    }

    private async Task<string> GetProfitSummaryAsync(
        [Description("Optional start date in yyyy-MM-dd format")] string? fromDate,
        [Description("Optional end date in yyyy-MM-dd format")] string? toDate,
        [Description("Optional location or city name to match against delivery address")] string? location)
    {
        var (startUtc, endUtc) = ParseDateWindow(fromDate, toDate);

        var salesQuery = ApplyLocationFilter(_context.Orders.AsNoTracking().AsQueryable(), location);
        if (startUtc.HasValue)
        {
            salesQuery = salesQuery.Where(o => o.CreatedAt >= startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            salesQuery = salesQuery.Where(o => o.CreatedAt < endUtc.Value);
        }

        var totalSales = await salesQuery.SumAsync(o => (decimal?)o.TotalAmount) ?? 0m;
        var totalExpense = await _context.Inventory
            .AsNoTracking()
            .SumAsync(i => (decimal?)i.UnitPrice * (decimal)i.StockAmount) ?? 0m;

        var netProfit = totalSales - totalExpense;
        var locationLabel = BuildLocationLabel(location);

        return $"Profit summary{locationLabel}: totalSales={totalSales:F2}, totalExpense={totalExpense:F2}, netProfit={netProfit:F2}.";
    }

    private async Task<string> GetTopMenuSummaryAsync(
        [Description("Number of items to return. Default 5. Min 1, max 20.")] int topN = 5,
        [Description("Optional start date in yyyy-MM-dd format")] string? fromDate = null,
        [Description("Optional end date in yyyy-MM-dd format")] string? toDate = null,
        [Description("Optional location or city name to match against delivery address")] string? location = null)
    {
        topN = Math.Clamp(topN, 1, 20);
        var (startUtc, endUtc) = ParseDateWindow(fromDate, toDate);

        var query = ApplyLocationFilter(_context.OrderItems
            .AsNoTracking()
            .Include(i => i.Order)
            .Where(i => i.Order != null)
            .AsQueryable(), location);

        if (startUtc.HasValue)
        {
            query = query.Where(i => i.Order!.CreatedAt >= startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            query = query.Where(i => i.Order!.CreatedAt < endUtc.Value);
        }

        var topItems = await query
            .GroupBy(i => i.ProductName)
            .Select(g => new
            {
                ProductName = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.UnitPrice * x.Quantity)
            })
            .OrderByDescending(x => x.Quantity)
            .ThenByDescending(x => x.Revenue)
            .Take(topN)
            .ToListAsync();

        var locationLabel = BuildLocationLabel(location);

        if (topItems.Count == 0)
        {
            return $"Top menu summary{locationLabel}: no order items found for the selected filters.";
        }

        var rows = topItems.Select((item, index) =>
            $"{index + 1}. {item.ProductName} | qty={item.Quantity} | revenue={item.Revenue:F2}");

        return $"Top menu summary{locationLabel}:\n" + string.Join("\n", rows);
    }

    private static IQueryable<Restaurant.API.Models.OrderRecord> ApplyLocationFilter(
        IQueryable<Restaurant.API.Models.OrderRecord> query,
        string? location)
    {
        var normalizedLocation = NormalizeLocation(location);
        if (normalizedLocation is null)
        {
            return query;
        }

        return query.Where(order => order.DeliveryAddress != null &&
            EF.Functions.Like(order.DeliveryAddress, $"%{normalizedLocation}%"));
    }

    private static IQueryable<OrderLineItem> ApplyLocationFilter(
        IQueryable<OrderLineItem> query,
        string? location)
    {
        var normalizedLocation = NormalizeLocation(location);
        if (normalizedLocation is null)
        {
            return query;
        }

        return query.Where(item => item.Order != null &&
            item.Order.DeliveryAddress != null &&
            EF.Functions.Like(item.Order.DeliveryAddress, $"%{normalizedLocation}%"));
    }

    private static string? NormalizeLocation(string? location)
    {
        var normalizedLocation = location?.Trim();
        return string.IsNullOrWhiteSpace(normalizedLocation) ? null : normalizedLocation;
    }

    private static string BuildLocationLabel(string? location)
    {
        var normalizedLocation = NormalizeLocation(location);
        return normalizedLocation is null ? string.Empty : $" for location '{normalizedLocation}'";
    }

    private static string? ExtractLocationHint(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var normalized = $" {prompt.Trim()} ";
        string[] markers = [" in ", " around ", " near "];
        string[] stopTokens = [" this ", " today", " yesterday", " last ", " next ", " top ", " sale", " sales", " revenue", " profit", " expense", " expenses", " order", " orders", " paid "];

        foreach (var marker in markers)
        {
            var markerIndex = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                continue;
            }

            var fragment = normalized[(markerIndex + marker.Length)..].Trim();
            if (fragment.Length == 0)
            {
                continue;
            }

            var stopIndex = fragment.Length;
            foreach (var stopToken in stopTokens)
            {
                var candidateIndex = fragment.IndexOf(stopToken, StringComparison.OrdinalIgnoreCase);
                if (candidateIndex >= 0 && candidateIndex < stopIndex)
                {
                    stopIndex = candidateIndex;
                }
            }

            var location = fragment[..stopIndex].Trim().Trim(',', '.', '!', '?');
            if (!string.IsNullOrWhiteSpace(location))
            {
                return location;
            }
        }

        return null;
    }

    private static (DateTime? StartUtc, DateTime? EndUtc) ParseDateWindow(string? fromDate, string? toDate)
    {
        DateTime? startUtc = null;
        DateTime? endUtc = null;

        if (!string.IsNullOrWhiteSpace(fromDate)
            && DateTime.TryParseExact(fromDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startDate))
        {
            startUtc = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        }

        if (!string.IsNullOrWhiteSpace(toDate)
            && DateTime.TryParseExact(toDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var endDate))
        {
            endUtc = DateTime.SpecifyKind(endDate.Date.AddDays(1), DateTimeKind.Utc);
        }

        return (startUtc, endUtc);
    }
}
