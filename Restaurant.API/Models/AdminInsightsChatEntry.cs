using System.ComponentModel.DataAnnotations;

namespace Restaurant.API.Models
{
    public class AdminInsightsChatEntry
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(120)]
        public string SessionKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(16)]
        public string Role { get; set; } = string.Empty;

        [Required]
        public string PayloadJson { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
