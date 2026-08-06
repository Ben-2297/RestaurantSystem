namespace Restaurant.API.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty; // This acts as their Email address
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; // Admin, KitchenStaff, User
        
        // --- EMAIL VERIFICATION SYSTEM FIELDS ---
        public bool IsVerified { get; set; } = false; // Starts off unverified by default
        public string? VerificationToken { get; set; }

        public UserProfile? Profile { get; set; }
    }
}