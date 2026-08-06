using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Restaurant.API.Models
{
    public class OrderLineItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        [MaxLength(120)]
        public string ProductName { get; set; } = string.Empty;

        [Required]
        [MaxLength(80)]
        public string Category { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public bool IsHalfOption { get; set; }

        public decimal UnitPrice { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string ImageUrl { get; set; } = string.Empty;

        [ForeignKey("OrderId")]
        [JsonIgnore]
        public OrderRecord? Order { get; set; }
    }
}
