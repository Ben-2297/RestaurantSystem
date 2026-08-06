using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Restaurant.API.Models
{
    public class UserProfile
    {
        [Key]
        public int Id { get; set; }

        // This links directly to the primary key of your existing User table
        public int UserId { get; set; }

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Address { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        // Navigation property for Entity Framework. 
        // JsonIgnore prevents infinite circular reference loops during serialization.
        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }
    }
}