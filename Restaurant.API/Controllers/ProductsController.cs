using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.API.Data;
using Restaurant.API.Models;

namespace Restaurant.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private readonly DataContext _context;

        public ProductsController(DataContext context)
        {
            _context = context;
        }

        // GET: api/products
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductItem>>> GetProducts()
        {
            return await _context.ProductItems
                .Include(p => p.RecipeIngredients)
                    .ThenInclude(r => r.InventoryItem)
                .ToListAsync();
        }

        // POST: api/products
        [HttpPost]
        public async Task<ActionResult<ProductItem>> CreateProduct(ProductSavePayload payload)
        {
            if (payload.Ingredients != null)
            {
                foreach (var ing in payload.Ingredients)
                {
                    if (ing.Quantity <= 0)
                    {
                        return BadRequest("Ingredient quantity must be greater than zero.");
                    }

                    var inventoryItem = await _context.Inventory.FirstOrDefaultAsync(i => i.Id == ing.InventoryItemId);
                    if (inventoryItem is null)
                    {
                        return BadRequest($"Inventory item {ing.InventoryItemId} was not found.");
                    }

                    if (inventoryItem.StockAmount < ing.Quantity)
                    {
                        return BadRequest($"Insufficient stock for {inventoryItem.Name}. Required: {ing.Quantity}, Available: {inventoryItem.StockAmount}.");
                    }

                    inventoryItem.StockAmount -= ing.Quantity;
                }
            }

            var product = new ProductItem
            {
                Name = payload.Name,
                Price = payload.Price,
                Description = payload.Description,
                IsAvailable = payload.IsAvailable,
                ImageUrl = payload.ImageUrl,
                CreatedAt = DateTime.Now
            };

            _context.ProductItems.Add(product);
            await _context.SaveChangesAsync(); // Saves product first to generate Id

            if (payload.Ingredients != null)
            {
                foreach (var ing in payload.Ingredients)
                {
                    _context.ProductRecipes.Add(new ProductRecipe
                    {
                        ProductItemId = product.Id,
                        InventoryItemId = ing.InventoryItemId,
                        QuantityRequired = ing.Quantity
                    });
                }
                await _context.SaveChangesAsync();
            }

            return Ok(product);
        }

        // PUT: api/products/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, ProductSavePayload payload)
        {
            var product = await _context.ProductItems
                .Include(p => p.RecipeIngredients)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound("Product not found");

            var existingRecipeByInventory = product.RecipeIngredients
                .GroupBy(r => r.InventoryItemId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.QuantityRequired));

            var newRecipeByInventory = (payload.Ingredients ?? new List<RecipeLinkPayload>())
                .GroupBy(i => i.InventoryItemId)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

            foreach (var kvp in newRecipeByInventory)
            {
                if (kvp.Value <= 0)
                {
                    return BadRequest("Ingredient quantity must be greater than zero.");
                }
            }

            foreach (var kvp in newRecipeByInventory)
            {
                var inventoryItem = await _context.Inventory.FirstOrDefaultAsync(i => i.Id == kvp.Key);
                if (inventoryItem is null)
                {
                    return BadRequest($"Inventory item {kvp.Key} was not found.");
                }

                existingRecipeByInventory.TryGetValue(kvp.Key, out var existingQty);
                var additionalRequired = kvp.Value - existingQty;

                // Deduct only the extra ingredient amount when recipe quantities increase.
                if (additionalRequired > 0)
                {
                    if (inventoryItem.StockAmount < additionalRequired)
                    {
                        return BadRequest($"Insufficient stock for {inventoryItem.Name}. Required additional: {additionalRequired}, Available: {inventoryItem.StockAmount}.");
                    }

                    inventoryItem.StockAmount -= additionalRequired;
                }
            }

            // 1. Update master values
            product.Name = payload.Name;
            product.Price = payload.Price;
            product.Description = payload.Description;
            product.IsAvailable = payload.IsAvailable;
            product.ImageUrl = payload.ImageUrl;

            // 2. Clear out old recipes to make replacing clean
            _context.ProductRecipes.RemoveRange(product.RecipeIngredients);

            // 3. Insert newly synced ingredients mappings
            if (payload.Ingredients != null)
            {
                foreach (var ing in payload.Ingredients)
                {
                    _context.ProductRecipes.Add(new ProductRecipe
                    {
                        ProductItemId = product.Id,
                        InventoryItemId = ing.InventoryItemId,
                        QuantityRequired = ing.Quantity
                    });
                }
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/products/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.ProductItems
                .Include(p => p.RecipeIngredients)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound("Product not found");

            // Cleanly remove child recipes from relational records first before dropping master row
            _context.ProductRecipes.RemoveRange(product.RecipeIngredients);
            _context.ProductItems.Remove(product);

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // Data Transfer Objects matching your frontend models perfectly
        public class ProductSavePayload
        {
            public string Name { get; set; } = string.Empty;
            public decimal Price { get; set; }
            public string Description { get; set; } = string.Empty;
            public bool IsAvailable { get; set; }
            public string ImageUrl { get; set; } = string.Empty;
            public List<RecipeLinkPayload> Ingredients { get; set; } = new();
        }

        public class RecipeLinkPayload
        {
            public int InventoryItemId { get; set; }
            public double Quantity { get; set; }
        }
    }
}