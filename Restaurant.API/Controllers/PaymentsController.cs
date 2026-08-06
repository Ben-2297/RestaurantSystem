using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.API.Data;
using Restaurant.API.Models;
using Stripe;
using Stripe.Checkout;
using System.Security.Claims;

namespace Restaurant.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly DataContext _context;
        private readonly IConfiguration _configuration;

        public PaymentsController(DataContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet("admin")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<PaymentRecord>>> GetAllPayments()
        {
            return await _context.Payments
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<PaymentRecord>>> GetUserPayments()
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (currentUser is null)
            {
                return Unauthorized();
            }

            return await _context.Payments
                .Where(p => p.UserId == currentUser.Id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        [AllowAnonymous]
        [HttpGet("public-config")]
        public ActionResult<StripePublicConfigResponseDto> GetPublicConfig()
        {
            var publishableKey = _configuration["Stripe:PublishableKey"];
            if (string.IsNullOrWhiteSpace(publishableKey))
            {
                return BadRequest("Stripe publishable key is missing.");
            }

            return Ok(new StripePublicConfigResponseDto
            {
                PublishableKey = publishableKey
            });
        }

        [HttpPost("create")]
        public async Task<ActionResult<PaymentCreateResponseDto>> CreatePayment([FromBody] PaymentCreateRequest request)
        {
            if (request.OrderId <= 0)
            {
                return BadRequest("A valid order ID is required.");
            }

            if (string.IsNullOrWhiteSpace(request.PaymentMethodId))
            {
                return BadRequest("Stripe PaymentMethod ID is required.");
            }

            if (string.IsNullOrWhiteSpace(_configuration["Stripe:SecretKey"]))
            {
                return BadRequest("Stripe configuration is missing.");
            }

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId);

            if (order is null)
            {
                return NotFound("Order not found.");
            }

            if (!string.Equals(order.Status, "Confirm", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("The order must be confirmed by the admin before payment can be processed.");
            }

            var username = User.FindFirstValue(ClaimTypes.Name);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (currentUser is null)
            {
                return Unauthorized();
            }

            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];

            var paymentIntentService = new PaymentIntentService();
            var paymentMethodService = new PaymentMethodService();

            PaymentMethod? paymentMethod = null;
            PaymentIntent paymentIntent;

            try
            {
                paymentMethod = await paymentMethodService.GetAsync(request.PaymentMethodId);

                paymentIntent = await paymentIntentService.CreateAsync(new PaymentIntentCreateOptions
                {
                    Amount = (long)(order.TotalAmount * 100),
                    Currency = "usd",
                    PaymentMethod = request.PaymentMethodId,
                    Confirm = true,
                    ConfirmationMethod = "automatic",
                    Metadata = new Dictionary<string, string>
                    {
                        ["orderId"] = order.Id.ToString(),
                        ["userId"] = currentUser.Id.ToString()
                    }
                });
            }
            catch (StripeException ex)
            {
                return BadRequest($"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
            }

            var payment = new PaymentRecord
            {
                OrderId = order.Id,
                UserId = currentUser.Id,
                CustomerName = order.CustomerName,
                PaymentMethod = "Credit Card",
                Amount = order.TotalAmount,
                StripePaymentIntentId = paymentIntent.Id,
                StripeClientSecret = paymentIntent.ClientSecret,
                CardLast4 = paymentMethod.Card?.Last4,
                Status = paymentIntent.Status == "succeeded" ? "Paid" : paymentIntent.Status,
                CreatedAt = DateTime.UtcNow
            };

            _context.Payments.Add(payment);
            order.IsPaid = paymentIntent.Status == "succeeded";
            if (order.IsPaid)
            {
                order.Status = "Cooking";
            }
            await _context.SaveChangesAsync();

            return Ok(new PaymentCreateResponseDto
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                Status = payment.Status,
                ClientSecret = payment.StripeClientSecret,
                Amount = payment.Amount,
                CreatedAt = payment.CreatedAt
            });
        }

        [HttpPost("create-checkout-session")]
        public async Task<ActionResult<CreateCheckoutSessionResponseDto>> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
        {
            if (request.OrderId <= 0)
            {
                return BadRequest("A valid order ID is required.");
            }

            if (string.IsNullOrWhiteSpace(_configuration["Stripe:SecretKey"]))
            {
                return BadRequest("Stripe configuration is missing.");
            }

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId);

            if (order is null)
            {
                return NotFound("Order not found.");
            }

            if (order.IsPaid)
            {
                return BadRequest("This order is already paid.");
            }

            if (!string.Equals(order.Status, "Confirm", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("The order must be confirmed by the admin before payment can be processed.");
            }

            var username = User.FindFirstValue(ClaimTypes.Name);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (currentUser is null)
            {
                return Unauthorized();
            }

            if (order.UserId != currentUser.Id && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];

            var requestBaseUrl = $"{Request.Scheme}://{Request.Host}";
            var configuredSuccessUrl = _configuration["Stripe:CheckoutSuccessUrl"];
            var configuredCancelUrl = _configuration["Stripe:CheckoutCancelUrl"];

            var successUrlBase = string.IsNullOrWhiteSpace(configuredSuccessUrl)
                ? $"{requestBaseUrl}/api/payments/checkout-success"
                : configuredSuccessUrl;
            var cancelUrl = string.IsNullOrWhiteSpace(configuredCancelUrl)
                ? $"{requestBaseUrl}/api/payments/checkout-cancel"
                : configuredCancelUrl;

            var sessionService = new SessionService();
            var session = await sessionService.CreateAsync(new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = $"{successUrlBase}?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = cancelUrl,
                ClientReferenceId = order.Id.ToString(),
                CustomerEmail = order.CustomerEmail,
                Metadata = new Dictionary<string, string>
                {
                    ["orderId"] = order.Id.ToString(),
                    ["userId"] = order.UserId.ToString()
                },
                LineItems = order.Items.Select(item => new SessionLineItemOptions
                {
                    Quantity = item.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "usd",
                        UnitAmount = (long)(item.UnitPrice * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.ProductName,
                            Description = item.IsHalfOption ? "Half portion" : "Full portion"
                        }
                    }
                }).ToList()
            });

            return Ok(new CreateCheckoutSessionResponseDto
            {
                OrderId = order.Id,
                CheckoutUrl = session.Url ?? string.Empty,
                SessionId = session.Id
            });
        }

        [HttpPost("verify-checkout-session")]
        public async Task<ActionResult<VerifyCheckoutSessionResponseDto>> VerifyCheckoutSession([FromBody] VerifyCheckoutSessionRequest request)
        {
            if (request.OrderId <= 0 || string.IsNullOrWhiteSpace(request.SessionId))
            {
                return BadRequest("OrderId and SessionId are required.");
            }

            if (string.IsNullOrWhiteSpace(_configuration["Stripe:SecretKey"]))
            {
                return BadRequest("Stripe configuration is missing.");
            }

            var username = User.FindFirstValue(ClaimTypes.Name);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (currentUser is null)
            {
                return Unauthorized();
            }

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId);
            if (order is null)
            {
                return NotFound("Order not found.");
            }

            if (order.UserId != currentUser.Id && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];

            Session session;
            try
            {
                var sessionService = new SessionService();
                session = await sessionService.GetAsync(request.SessionId);
            }
            catch (StripeException ex)
            {
                return BadRequest($"Stripe error: {ex.StripeError?.Message ?? ex.Message}");
            }

            if (!string.Equals(session.ClientReferenceId, order.Id.ToString(), StringComparison.Ordinal))
            {
                return BadRequest("Stripe session does not belong to this order.");
            }

            var wasMarkedPaid = await CompletePaidCheckoutSessionAsync(session, order);

            return Ok(new VerifyCheckoutSessionResponseDto
            {
                OrderId = order.Id,
                SessionId = session.Id,
                StripeStatus = session.Status ?? string.Empty,
                PaymentStatus = session.PaymentStatus ?? string.Empty,
                IsPaid = order.IsPaid,
                WasMarkedPaid = wasMarkedPaid
            });
        }

        [AllowAnonymous]
        [HttpGet("checkout-success")]
        public ContentResult CheckoutSuccess([FromQuery(Name = "session_id")] string? sessionId)
        {
            var encodedSession = System.Net.WebUtility.HtmlEncode(sessionId ?? string.Empty);
            var deepLink = $"restaurantapp://payments/checkout-success?session_id={Uri.EscapeDataString(sessionId ?? string.Empty)}";
            var encodedDeepLink = System.Net.WebUtility.HtmlEncode(deepLink);

                        var html = $@"<!doctype html>
<html>
        <head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1""><title>Payment Successful</title></head>
        <body style=""font-family:Arial,sans-serif;padding:24px;max-width:640px;margin:auto;"">
                <h2>Payment Received</h2>
                <p>Your payment was processed by Stripe.</p>
                <p>Returning to app automatically...</p>
                <p><a href=""{encodedDeepLink}"">Tap here if the app does not open automatically.</a></p>
                <p style=""font-size:12px;color:#666;"">Session: {encodedSession}</p>
                <script>
                    setTimeout(function() {{
                        window.location.href = {System.Text.Json.JsonSerializer.Serialize(deepLink)};
                    }}, 600);
                </script>
        </body>
</html>";

                        return Content(html, "text/html");
        }

        [AllowAnonymous]
        [HttpGet("checkout-cancel")]
        public ContentResult CheckoutCancel()
        {
            const string deepLink = "restaurantapp://payments/checkout-cancel";

                        var html = $@"<!doctype html>
<html>
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1""><title>Payment Canceled</title></head>
<body style=""font-family:Arial,sans-serif;padding:24px;max-width:640px;margin:auto;"">
<h2>Payment Canceled</h2>
<p>No charge was completed.</p>
<p>Returning to app automatically...</p>
<p><a href=""{deepLink}"">Tap here if the app does not open automatically.</a></p>
<script>
    setTimeout(function() {{
        window.location.href = {System.Text.Json.JsonSerializer.Serialize(deepLink)};
    }}, 600);
</script>
</body>
</html>";

                        return Content(html, "text/html");
        }

        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> StripeWebhook()
        {
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                return BadRequest("Stripe webhook secret is missing.");
            }

            var json = await new StreamReader(Request.Body).ReadToEndAsync();
            var signature = Request.Headers["Stripe-Signature"];

            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, signature, webhookSecret);
            }
            catch (Exception)
            {
                return BadRequest("Invalid Stripe webhook signature.");
            }

            if (stripeEvent.Type == EventTypes.CheckoutSessionCompleted)
            {
                var session = stripeEvent.Data.Object as Session;
                if (session is null)
                {
                    return Ok();
                }

                var metadataOrderId = session.Metadata is not null && session.Metadata.TryGetValue("orderId", out var value)
                    ? value
                    : session.ClientReferenceId;

                if (!int.TryParse(metadataOrderId, out var orderId))
                {
                    return Ok();
                }

                var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
                if (order is null)
                {
                    return Ok();
                }

                if (order.IsPaid)
                {
                    return Ok();
                }

                await CompletePaidCheckoutSessionAsync(session, order);
            }

            return Ok();
        }

        private async Task<bool> CompletePaidCheckoutSessionAsync(Session session, OrderRecord order)
        {
            var paymentSucceeded = string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase);

            if (!paymentSucceeded)
            {
                return false;
            }

            var existingPayment = !string.IsNullOrWhiteSpace(session.PaymentIntentId)
                ? await _context.Payments.FirstOrDefaultAsync(p => p.StripePaymentIntentId == session.PaymentIntentId)
                : null;

            if (existingPayment is null)
            {
                _context.Payments.Add(new PaymentRecord
                {
                    OrderId = order.Id,
                    UserId = order.UserId,
                    CustomerName = order.CustomerName,
                    PaymentMethod = "Stripe Checkout",
                    Amount = session.AmountTotal.HasValue ? session.AmountTotal.Value / 100m : order.TotalAmount,
                    StripePaymentIntentId = session.PaymentIntentId,
                    Status = "Paid",
                    CreatedAt = DateTime.UtcNow
                });
            }

            order.IsPaid = true;
            if (!string.Equals(order.Status, "Pick-up", StringComparison.OrdinalIgnoreCase))
            {
                order.Status = "Cooking";
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public class PaymentCreateRequest
        {
            public int OrderId { get; set; }
            public string PaymentMethodId { get; set; } = string.Empty;
        }

        public class PaymentCreateResponseDto
        {
            public int PaymentId { get; set; }
            public int OrderId { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? ClientSecret { get; set; }
            public decimal Amount { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class CreateCheckoutSessionRequest
        {
            public int OrderId { get; set; }
        }

        public class CreateCheckoutSessionResponseDto
        {
            public int OrderId { get; set; }
            public string SessionId { get; set; } = string.Empty;
            public string CheckoutUrl { get; set; } = string.Empty;
        }

        public class StripePublicConfigResponseDto
        {
            public string PublishableKey { get; set; } = string.Empty;
        }

        public class VerifyCheckoutSessionRequest
        {
            public int OrderId { get; set; }
            public string SessionId { get; set; } = string.Empty;
        }

        public class VerifyCheckoutSessionResponseDto
        {
            public int OrderId { get; set; }
            public string SessionId { get; set; } = string.Empty;
            public string StripeStatus { get; set; } = string.Empty;
            public string PaymentStatus { get; set; } = string.Empty;
            public bool IsPaid { get; set; }
            public bool WasMarkedPaid { get; set; }
        }
    }
}
