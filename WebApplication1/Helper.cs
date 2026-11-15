using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HomeNest.Models;
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

        // 32-byte encryption key for sensitive fields (Bank accounts)
        private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("HomeNest_Secure_Key_2026_TARUMT!");
        private static readonly byte[] EncryptionIV = Encoding.UTF8.GetBytes("HomeNest_IV_2026");

        // Profanity blacklist
        private static readonly string[] ProfanityList =
        [
            "badword", "scam", "cheat", "bastard", "idiot", "damn", "hell", "fuck", "shit", "bitch", "asshole"
        ];

        public Helper(IWebHostEnvironment env, IHttpContextAccessor http)
        {
            _env = env;
            _http = http;
        }

        // ==========================================
        // 1. Password Hashing (ASP.NET Core PBKDF2)
        // ==========================================
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

        // ==========================================
        // 2. Cookie Authentication Sign-In / Out
        // ==========================================
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

        // ==========================================
        // 3. File Upload & Webcam Processing
        // ==========================================
        public string? ValidatePhoto(IFormFile f)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(ext))
                return "Only JPG, PNG, and WEBP images are allowed.";

            if (f.Length > 8 * 1024 * 1024)
                return "Image size cannot exceed 8 MB.";

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

        public string SaveBase64Photo(string base64Data, string subFolder)
        {
            string folderPath = Path.Combine(_env.WebRootPath, "uploads", subFolder);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // Strip data:image/...;base64,
            int commaIndex = base64Data.IndexOf(',');
            string raw = commaIndex >= 0 ? base64Data[(commaIndex + 1)..] : base64Data;
            byte[] bytes = Convert.FromBase64String(raw);

            string fileName = Guid.NewGuid().ToString("N") + ".png";
            string fullPath = Path.Combine(folderPath, fileName);
            File.WriteAllBytes(fullPath, bytes);

            return fileName;
        }

        // ==========================================
        // 4. Sensitive Field Encryption & Masking
        // ==========================================
        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            using var aes = Aes.Create();
            aes.Key = EncryptionKey;
            aes.IV = EncryptionIV;
            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            try
            {
                using var aes = Aes.Create();
                aes.Key = EncryptionKey;
                aes.IV = EncryptionIV;
                using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                byte[] buffer = Convert.FromBase64String(cipherText);
                using var ms = new MemoryStream(buffer);
                using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
                using var sr = new StreamReader(cs);
                return sr.ReadToEnd();
            }
            catch
            {
                return cipherText;
            }
        }

        public string MaskBankAccount(string accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber)) return "N/A";
            string trimmed = accountNumber.Trim();
            if (trimmed.Length <= 4) return "****";
            return new string('*', trimmed.Length - 4) + trimmed[^4..];
        }

        // ==========================================
        // 5. Profanity Filter for Reviews
        // ==========================================
        public string FilterProfanity(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string result = text;
            foreach (var badWord in ProfanityList)
            {
                string pattern = @"\b" + badWord + @"\b";
                string replacement = badWord[0] + new string('*', Math.Max(1, badWord.Length - 1));
                result = System.Text.RegularExpressions.Regex.Replace(
                    result,
                    pattern,
                    replacement,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            return result;
        }

        // ==========================================
        // 6. Currency Converter (MYR, USD, SGD)
        // ==========================================
        public (decimal convertedAmount, string symbol) ConvertCurrency(decimal amountInMyr, string currency)
        {
            return currency?.ToUpper() switch
            {
                "USD" => (Math.Round(amountInMyr / 4.45m, 2), "$"),
                "SGD" => (Math.Round(amountInMyr / 3.32m, 2), "S$"),
                _ => (amountInMyr, "RM")
            };
        }

        // ==========================================
        // 7. System Audit Trail Logging
        // ==========================================
        public void LogAudit(DB db, int? userId, string userEmail, string action, string details)
        {
            try
            {
                string? ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                var log = new AuditLog
                {
                    UserId = userId,
                    UserEmail = string.IsNullOrEmpty(userEmail) ? "Guest" : userEmail,
                    Action = action,
                    Details = details,
                    IpAddress = ip,
                    Timestamp = DateTime.Now
                };
                db.AuditLogs.Add(log);
                db.SaveChanges();
            }
            catch
            {
                // Silently avoid breaking main flow if audit log table issue
            }
        }
    }
}
