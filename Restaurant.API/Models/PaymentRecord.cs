using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Restaurant.API.Models
{
    public class PaymentRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [MaxLength(120)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(40)]
        public string PaymentMethod { get; set; } = "Credit Card";

        public decimal Amount { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        [MaxLength(120)]
        public string? StripePaymentIntentId { get; set; }

        [MaxLength(255)]
        public string? StripeClientSecret { get; set; }

        [MaxLength(10)]
        public string? CardLast4 { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("OrderId")]
        [JsonIgnore]
        public OrderRecord? Order { get; set; }
    }
}
