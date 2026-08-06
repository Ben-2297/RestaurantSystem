using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Restaurant.API.Models
{
    public class OrderRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(120)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(120)]
        public string CustomerEmail { get; set; } = string.Empty;

        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        [MaxLength(255)]
        public string DeliveryAddress { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = "Order";

        public bool IsPaid { get; set; }

        public decimal TotalAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<OrderLineItem> Items { get; set; } = new();

        [JsonIgnore]
        public List<PaymentRecord> Payments { get; set; } = new();
    }
}
