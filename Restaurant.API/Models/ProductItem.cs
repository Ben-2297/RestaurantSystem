using System.ComponentModel.DataAnnotations;

namespace Restaurant.API.Models
{
    public class ProductItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public decimal Price { get; set; }

        [Required]
        public string Description { get; set; } = string.Empty; // Used for Category (e.g., Noodles)

        [Required]
        public bool IsAvailable { get; set; } = true;

        public string ImageUrl { get; set; } = "https://images.unsplash.com/photo-1546069901-ba9599a7e63c?w=150";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation property linking this product to its recipe ingredients
        public List<ProductRecipe> RecipeIngredients { get; set; } = new();
    }
}