using Microsoft.EntityFrameworkCore;
using Restaurant.API.Models;

namespace Restaurant.API.Data
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();

        // This tells EF Core to create the 'UserProfiles' table
        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

        // This tells EF Core to create the 'Inventory' table
        public DbSet<InventoryItem> Inventory => Set<InventoryItem>();

        // This tells EF Core to create the 'ProductItems' table
        public DbSet<ProductItem> ProductItems => Set<ProductItem>();

        // This tells EF Core to create the 'ProductRecipes' table
        public DbSet<ProductRecipe> ProductRecipes => Set<ProductRecipe>();

        public DbSet<OrderRecord> Orders => Set<OrderRecord>();

        public DbSet<OrderLineItem> OrderItems => Set<OrderLineItem>();

        public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

        public DbSet<AdminInsightsChatEntry> AdminInsightsChatHistory => Set<AdminInsightsChatEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProductItem>()
                .Property(p => p.Price)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<InventoryItem>()
                .Property(i => i.UnitPrice)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<OrderRecord>()
                .Property(o => o.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<OrderLineItem>()
                .Property(i => i.UnitPrice)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<PaymentRecord>()
                .Property(p => p.Amount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<AdminInsightsChatEntry>()
                .HasIndex(x => new { x.SessionKey, x.CreatedAtUtc });

            modelBuilder.Entity<AdminInsightsChatEntry>()
                .Property(x => x.PayloadJson)
                .HasColumnType("nvarchar(max)");

            modelBuilder.Entity<User>()
                .HasOne(u => u.Profile)
                .WithOne(p => p.User)
                .HasForeignKey<UserProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrderRecord>()
                .HasMany(o => o.Items)
                .WithOne(i => i.Order)
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrderRecord>()
                .HasMany(o => o.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}