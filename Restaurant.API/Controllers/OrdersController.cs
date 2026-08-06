using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.API.Data;
using Restaurant.API.Models;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Restaurant.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly DataContext _context;

        public OrdersController(DataContext context)
        {
            _context = context;
        }

        [HttpPost("checkout")]
        public async Task<ActionResult<OrderCheckoutResponseDto>> CreateOrder([FromBody] OrderCheckoutRequest request)
        {
            if (request is null || request.Items.Count == 0)
            {
                return BadRequest("At least one order item is required.");
            }

            var pickupNote = string.IsNullOrWhiteSpace(request.PickupNotes)
                ? (string.IsNullOrWhiteSpace(request.DeliveryAddress) ? "No pickup note provided." : request.DeliveryAddress)
                : request.PickupNotes;

            var username = User.FindFirstValue(ClaimTypes.Name);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (currentUser is null)
            {
                return Unauthorized();
            }

            var order = new OrderRecord
            {
                UserId = request.UserId > 0 ? request.UserId : currentUser.Id,
                CustomerName = request.CustomerName,
                CustomerEmail = request.CustomerEmail,
                PhoneNumber = request.PhoneNumber,
                DeliveryAddress = pickupNote,
                Status = "Order",
                TotalAmount = request.Items.Sum(i => i.UnitPrice * i.Quantity),
                CreatedAt = DateTime.UtcNow
            };

            foreach (var item in request.Items)
            {
                var product = await _context.ProductItems.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product is null)
                {
                    return BadRequest($"Product {item.ProductId} was not found.");
                }

                if (!product.IsAvailable)
                {
                    return BadRequest($"Product {product.Name} is currently unavailable.");
                }

                order.Items.Add(new OrderLineItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Category = product.Description,
                    Quantity = item.Quantity,
                    IsHalfOption = item.IsHalfOption,
                    UnitPrice = item.UnitPrice,
                    ImageUrl = product.ImageUrl
                });
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            return Ok(new OrderCheckoutResponseDto
            {
                OrderId = order.Id,
                Status = order.Status,
                TotalAmount = order.TotalAmount,
                CreatedAt = order.CreatedAt,
                Items = order.Items.Select(i => new OrderLineItemDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Category = i.Category,
                    Quantity = i.Quantity,
                    IsHalfOption = i.IsHalfOption,
                    UnitPrice = i.UnitPrice,
                    ImageUrl = i.ImageUrl
                }).ToList()
            });
        }

        [HttpGet("admin")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<OrderRecord>>> GetAllOrders()
        {
            return await _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .AsSplitQuery()
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
        }

        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<OrderRecord>>> GetUserOrders()
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (currentUser is null)
            {
                return Unauthorized();
            }

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.UserId == currentUser.Id)
                .Include(o => o.Items)
                .AsSplitQuery()
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return Ok(orders);
        }

        [HttpPatch("{id}/status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] OrderStatusUpdateRequest request)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order is null)
            {
                return NotFound();
            }

            var requestedStatus = request.Status?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestedStatus))
            {
                return BadRequest("Status is required.");
            }

            var currentStatus = order.Status?.Trim() ?? string.Empty;
            var isCurrentOrder = string.Equals(currentStatus, "Order", StringComparison.OrdinalIgnoreCase);
            var isCurrentConfirm = string.Equals(currentStatus, "Confirm", StringComparison.OrdinalIgnoreCase);
            var isCurrentCooking = string.Equals(currentStatus, "Cooking", StringComparison.OrdinalIgnoreCase);

            var movingToConfirm = string.Equals(requestedStatus, "Confirm", StringComparison.OrdinalIgnoreCase);
            var movingToCooking = string.Equals(requestedStatus, "Cooking", StringComparison.OrdinalIgnoreCase);
            var movingToPickup = string.Equals(requestedStatus, "Pick-up", StringComparison.OrdinalIgnoreCase);

            var isValidTransition = (isCurrentOrder && movingToConfirm)
                || (isCurrentConfirm && movingToCooking && order.IsPaid)
                || (isCurrentCooking && movingToPickup)
                || string.Equals(currentStatus, requestedStatus, StringComparison.OrdinalIgnoreCase);

            if (!isValidTransition)
            {
                return BadRequest("Invalid status transition. Allowed flow: Order -> Confirm -> Cooking -> Pick-up.");
            }

            order.Status = requestedStatus;
            await _context.SaveChangesAsync();

            return Ok(order);
        }

        public class OrderCheckoutRequest
        {
            public int UserId { get; set; }
            public string CustomerName { get; set; } = string.Empty;
            public string CustomerEmail { get; set; } = string.Empty;
            public string PhoneNumber { get; set; } = string.Empty;
            public string PickupNotes { get; set; } = string.Empty;

            [JsonPropertyName("deliveryAddress")]
            public string DeliveryAddress { get; set; } = string.Empty;
            public List<OrderCheckoutItemDto> Items { get; set; } = new();
        }

        public class OrderCheckoutItemDto
        {
            public int ProductId { get; set; }
            public int Quantity { get; set; }
            public bool IsHalfOption { get; set; }
            public decimal UnitPrice { get; set; }
        }

        public class OrderLineItemDto
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public int Quantity { get; set; }
            public bool IsHalfOption { get; set; }
            public decimal UnitPrice { get; set; }
            public string ImageUrl { get; set; } = string.Empty;
        }

        public class OrderCheckoutResponseDto
        {
            public int OrderId { get; set; }
            public string Status { get; set; } = "Order";
            public decimal TotalAmount { get; set; }
            public DateTime CreatedAt { get; set; }
            public List<OrderLineItemDto> Items { get; set; } = new();
        }

        public class OrderStatusUpdateRequest
        {
            public string Status { get; set; } = "Confirm";
        }
    }
}
