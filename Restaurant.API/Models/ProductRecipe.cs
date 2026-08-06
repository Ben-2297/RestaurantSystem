using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Restaurant.API.Models
{
    public class ProductRecipe
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ProductItemId { get; set; }

        [Required]
        public int InventoryItemId { get; set; }

        [Required]
        public double QuantityRequired { get; set; }

        [Required]
        public string QuantityUnit { get; set; } = string.Empty;

        [ForeignKey("ProductItemId")]
        public ProductItem? ProductItem { get; set; }

        [ForeignKey("InventoryItemId")]
        public InventoryItem? InventoryItem { get; set; }
    }
}