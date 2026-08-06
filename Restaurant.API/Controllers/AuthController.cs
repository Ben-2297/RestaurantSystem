using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Restaurant.API.Data;
using Restaurant.API.Models;
using Restaurant.API.Services; // Imported the service folder reference
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Restaurant.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DataContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService; // Field added for email engine context

        // STEP 4/5: Injected the new IEmailService framework right through the constructor
        public AuthController(DataContext context, IConfiguration configuration, IEmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<LoginResponseDto>> Register(UserRegisterDto request)
        {
            // Check if account username (email) already exists
            if (await _context.Users.AnyAsync(u => u.Username == request.Email))
            {
                return BadRequest("An account with this email already exists.");
            }

            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                Username = request.Email, // Map the incoming mobile email to your database Username column
                PasswordHash = passwordHash,
                Role = "User", // Standard sign-ups default to customer role
                IsVerified = false, // Starts unverified by default until verified through email
                VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(64))
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Link the user's profile details submitted from the sign-up page instantly
            var profile = new UserProfile
            {
                UserId = user.Id,
                FullName = request.FullName,
                Address = request.Address,
                PhoneNumber = request.PhoneNumber
            };

            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync();

            // --- REAL SENDGRID EMAIL TRANSMISSION STEP ---
            // Build the live backend API server verify endpoint path
            var verificationLink = $"http://100.103.230.85:5123/api/auth/verify?token={user.VerificationToken}";
            
            try
            {
                // FIRE THE ENGINE TO THE USER'S INBOX!
                await _emailService.SendVerificationEmailAsync(user.Username, profile.FullName, verificationLink);
                Console.WriteLine($"\n[SUCCESS] Production Verification Email dispatched to {user.Username}!");
            }
            catch (Exception ex)
            {
                // Fallback logging catch block so local backend execution does not crash if API keys are misconfigured
                Console.WriteLine($"\n[EMAIL ERROR] Failed to send email via SendGrid: {ex.Message}");
                Console.WriteLine($"[FALLBACK DIAL LINK]: {verificationLink}\n");
            }

            // --- AUTO-LOGIN GENERATION ON SIGN UP ---
            string token = CreateToken(user);

            var loginResponse = new LoginResponseDto
            {
                Token = token,
                UserId = user.Id,
                FullName = profile.FullName,
                Address = profile.Address,
                PhoneNumber = profile.PhoneNumber,
                IsVerified = user.IsVerified // Returns false to let the mobile profile icon show "Unverified"
            };

            return Ok(loginResponse);
        }

        [HttpGet("verify")]
        public async Task<ActionResult> VerifyAccount(string token)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.VerificationToken == token);
            if (user == null)
            {
                return BadRequest("Invalid or expired verification token.");
            }

            user.IsVerified = true;
            user.VerificationToken = null; // Clear out the token once utilized
            await _context.SaveChangesAsync();

            return Content("<h3>Your account has been verified successfully! You can now log in and place orders on the mobile app.</h3>", "text/html");
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponseDto>> Login(UserLoginDto request)
        {
            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Username == request.Username);
            
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return BadRequest("Wrong username or password.");
            }

            // --- EMAIL VERIFICATION GUARD RULE FOR MANUAL LOGINS ---
            if (!user.IsVerified)
            {
                return BadRequest("Your account is unverified. Please check your email inbox to verify your account before logging in.");
            }

            // --- CLIENT-SPECIFIC ROLE RESTRICTION BLOCK ---
            if (!string.IsNullOrWhiteSpace(request.ClientType))
            {
                if (request.ClientType.Equals("Mobile", StringComparison.OrdinalIgnoreCase) &&
                    !user.Role.Equals("User", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Access denied. Only standard User accounts are allowed to access the mobile application.");
                }

                if (request.ClientType.Equals("Web", StringComparison.OrdinalIgnoreCase) &&
                    !user.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
                    !user.Role.Equals("KitchenStaff", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest("Access denied. Only Admin and KitchenStaff accounts are allowed to access the web application.");
                }
            }

            string token = CreateToken(user);

            var loginResponse = new LoginResponseDto
            {
                Token = token,
                UserId = user.Id,
                FullName = user.Profile?.FullName ?? string.Empty,
                Address = user.Profile?.Address ?? string.Empty,
                PhoneNumber = user.Profile?.PhoneNumber ?? string.Empty,
                IsVerified = user.IsVerified
            };

            return Ok(loginResponse);
        }

        private string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration.GetSection("AppSettings:Token").Value!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    // Comprehensive registration transmission contract
    public class UserRegisterDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
    }
    
    public class UserLoginDto 
    { 
        public string Username { get; set; } = string.Empty; 
        public string Password { get; set; } = string.Empty; 
        public string ClientType { get; set; } = string.Empty; 
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public bool IsVerified { get; set; }
    }
}