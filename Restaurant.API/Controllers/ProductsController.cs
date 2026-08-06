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

                    if (string.IsNullOrWhiteSpace(ing.Unit))
                    {
                        return BadRequest("Ingredient unit is required.");
                    }

                    var inventoryItem = await _context.Inventory.FirstOrDefaultAsync(i => i.Id == ing.InventoryItemId);
                    if (inventoryItem is null)
                    {
                        return BadRequest($"Inventory item {ing.InventoryItemId} was not found.");
                    }

                    if (!TryConvertQuantity(ing.Quantity, ing.Unit, inventoryItem.Unit, out var requiredInventoryQty))
                    {
                        return BadRequest($"Cannot convert ingredient unit '{ing.Unit}' to inventory unit '{inventoryItem.Unit}'.");
                    }

                    if (inventoryItem.StockAmount < requiredInventoryQty)
                    {
                        return BadRequest($"Insufficient stock for {inventoryItem.Name}. Required: {requiredInventoryQty} {inventoryItem.Unit}, Available: {inventoryItem.StockAmount} {inventoryItem.Unit}.");
                    }

                    inventoryItem.StockAmount -= requiredInventoryQty;
                }
            }

            var product = new ProductItem
            {
                Name = payload.Name,
                Category = payload.Category,
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
                        QuantityRequired = ing.Quantity,
                        QuantityUnit = ing.Unit
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

            var existingRecipeByInventory = new Dictionary<int, double>();
            foreach (var recipe in product.RecipeIngredients)
            {
                var inventoryItem = await _context.Inventory.FirstOrDefaultAsync(i => i.Id == recipe.InventoryItemId);
                if (inventoryItem is null)
                {
                    return BadRequest($"Inventory item {recipe.InventoryItemId} was not found.");
                }

                if (!TryConvertQuantity(recipe.QuantityRequired, recipe.QuantityUnit, inventoryItem.Unit, out var normalizedQuantity))
                {
                    return BadRequest($"Cannot convert existing recipe unit '{recipe.QuantityUnit}' to inventory unit '{inventoryItem.Unit}'.");
                }

                existingRecipeByInventory[recipe.InventoryItemId] = existingRecipeByInventory.GetValueOrDefault(recipe.InventoryItemId) + normalizedQuantity;
            }

            var newRecipeByInventory = new Dictionary<int, double>();
            foreach (var ingredient in payload.Ingredients ?? new List<RecipeLinkPayload>())
            {
                if (ingredient.Quantity <= 0)
                {
                    return BadRequest("Ingredient quantity must be greater than zero.");
                }

                if (string.IsNullOrWhiteSpace(ingredient.Unit))
                {
                    return BadRequest("Ingredient unit is required.");
                }

                var inventoryItem = await _context.Inventory.FirstOrDefaultAsync(i => i.Id == ingredient.InventoryItemId);
                if (inventoryItem is null)
                {
                    return BadRequest($"Inventory item {ingredient.InventoryItemId} was not found.");
                }

                if (!TryConvertQuantity(ingredient.Quantity, ingredient.Unit, inventoryItem.Unit, out var normalizedQuantity))
                {
                    return BadRequest($"Cannot convert ingredient unit '{ingredient.Unit}' to inventory unit '{inventoryItem.Unit}'.");
                }

                newRecipeByInventory[ingredient.InventoryItemId] = newRecipeByInventory.GetValueOrDefault(ingredient.InventoryItemId) + normalizedQuantity;
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
                        return BadRequest($"Insufficient stock for {inventoryItem.Name}. Required additional: {additionalRequired} {inventoryItem.Unit}, Available: {inventoryItem.StockAmount} {inventoryItem.Unit}.");
                    }

                    inventoryItem.StockAmount -= additionalRequired;
                }
            }

            // 1. Update master values
            product.Name = payload.Name;
            product.Category = payload.Category;
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
                        QuantityRequired = ing.Quantity,
                        QuantityUnit = ing.Unit
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
            public string Category { get; set; } = string.Empty;
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
            public string Unit { get; set; } = string.Empty;
        }

        private static bool TryConvertQuantity(double quantity, string fromUnit, string toUnit, out double convertedQuantity)
        {
            convertedQuantity = 0;
            var source = NormalizeUnit(fromUnit);
            var target = NormalizeUnit(toUnit);

            if (source == null || target == null)
            {
                return false;
            }

            if (source == target)
            {
                convertedQuantity = quantity;
                return true;
            }

            var sourceCategory = GetUnitCategory(source);
            var targetCategory = GetUnitCategory(target);
            if (sourceCategory != targetCategory || sourceCategory == UnitCategory.Unknown)
            {
                return false;
            }

            if (sourceCategory == UnitCategory.Weight)
            {
                var grams = source switch
                {
                    "g" => quantity,
                    "kg" => quantity * 1000,
                    "lb" => quantity * 453.59237,
                    _ => 0
                };

                convertedQuantity = target switch
                {
                    "g" => grams,
                    "kg" => grams / 1000,
                    "lb" => grams / 453.59237,
                    _ => 0
                };

                return convertedQuantity > 0;
            }

            if (sourceCategory == UnitCategory.Volume)
            {
                convertedQuantity = quantity; // Only liters supported at this time
                return true;
            }

            return false;
        }

        private static string? NormalizeUnit(string unit)
        {
            var normalized = unit.Trim().ToLowerInvariant();
            return normalized switch
            {
                "kg" or "kilogram" or "kilograms" => "kg",
                "g" or "gram" or "grams" => "g",
                "lb" or "lbs" or "pound" or "pounds" => "lb",
                "l" or "liter" or "liters" or "litre" or "litres" => "l",
                _ => null
            };
        }

        private enum UnitCategory
        {
            Unknown,
            Weight,
            Volume
        }

        private static UnitCategory GetUnitCategory(string unit)
        {
            return unit switch
            {
                "kg" => UnitCategory.Weight,
                "g" => UnitCategory.Weight,
                "lb" => UnitCategory.Weight,
                "l" => UnitCategory.Volume,
                _ => UnitCategory.Unknown
            };
        }
    }
}