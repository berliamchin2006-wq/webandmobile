using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace HomeNest
{
    public class Helper
    {
        private readonly IWebHostEnvironment _env;
        private readonly IHttpContextAccessor _http;
        private readonly PasswordHasher<object> _hasher = new();

        public Helper(IWebHostEnvironment env, IHttpContextAccessor http)
        {
            _env = env;
            _http = http;
        }

        public string HashPassword(string password)
        {
            return _hasher.HashPassword(0, password);
        }

        public bool VerifyPassword(string hash, string password)
        {
            var result = _hasher.VerifyHashedPassword(0, hash, password);
            return result == PasswordVerificationResult.Success ||
                   result == PasswordVerificationResult.SuccessRehashNeeded;
        }

        public async Task SignInAsync(int userId, string email, string fullName, string role, bool rememberMe)
        {
            List<Claim> claims =
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, email),
                new Claim("FullName", fullName),
                new Claim(ClaimTypes.Role, role)
            ];

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            var properties = new AuthenticationProperties
            {
                IsPersistent = rememberMe
            };

            if (_http.HttpContext != null)
            {
                await _http.HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    properties);
            }
        }

        public async Task SignOutAsync()
        {
            if (_http.HttpContext != null)
            {
                await _http.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }

        public string? ValidatePhoto(IFormFile f)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(ext))
                return "Only JPG, PNG, and WEBP images are allowed.";

            if (f.Length > 5 * 1024 * 1024)
                return "Image size cannot exceed 5 MB.";

            return null;
        }

        public string SavePhoto(IFormFile f, string subFolder)
        {
            string folderPath = Path.Combine(_env.WebRootPath, "uploads", subFolder);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(f.FileName).ToLowerInvariant();
            string fullPath = Path.Combine(folderPath, fileName);

            using var stream = new FileStream(fullPath, FileMode.Create);
            f.CopyTo(stream);

            return fileName;
        }
    }
}