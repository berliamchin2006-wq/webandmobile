using System.Security.Claims;
using HomeNest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeNest.Controllers
{
    public class AccountController : Controller
    {
        private readonly DB _db;
        private readonly Helper _helper;
        private readonly HomeNest.Services.EmailService _emailService;

        public AccountController(DB db, Helper helper, HomeNest.Services.EmailService emailService)
        {
            _db = db;
            _helper = helper;
            _emailService = emailService;
        }

        // ==========================================
        // Remote Email Validation
        // ==========================================
        [AcceptVerbs("GET", "POST")]
        public IActionResult CheckEmail(string email)
        {
            bool exists = _db.Users.Any(u => u.Email.ToLower() == email.Trim().ToLower());
            return Json(!exists);
        }

        // ==========================================
        // Dynamic SVG Anti-Bot CAPTCHA
        // ==========================================
        [HttpGet]
        public IActionResult CaptchaImage()
        {
            const string chars = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
            var rnd = new Random();
            string code = new string(Enumerable.Repeat(chars, 4).Select(s => s[rnd.Next(s.Length)]).ToArray());
            HttpContext.Session.SetString("CaptchaCode", code);

            int width = 130;
            int height = 44;
            var sb = new System.Text.StringBuilder();
            sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>");
            sb.Append("<rect width='100%' height='100%' fill='#f8fafc' stroke='#cbd5e1' rx='6' />");

            // Noise lines
            for (int i = 0; i < 4; i++)
            {
                int x1 = rnd.Next(width);
                int y1 = rnd.Next(height);
                int x2 = rnd.Next(width);
                int y2 = rnd.Next(height);
                string color = i % 2 == 0 ? "#cbd5e1" : "#94a3b8";
                sb.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{color}' stroke-width='1.5' />");
            }

            // Characters with rotation
            string[] fontColors = { "#0f766e", "#0369a1", "#4338ca", "#b45309" };
            for (int i = 0; i < code.Length; i++)
            {
                int x = 16 + i * 26;
                int y = rnd.Next(28, 33);
                int rot = rnd.Next(-18, 18);
                string fill = fontColors[i % fontColors.Length];
                sb.Append($"<text x='{x}' y='{y}' fill='{fill}' font-size='24' font-weight='800' font-family='Arial, sans-serif' transform='rotate({rot} {x} {y})'>{code[i]}</text>");
            }

            sb.Append("</svg>");
            return Content(sb.ToString(), "image/svg+xml");
        }

        // ==========================================
        // 1.1 Authentication: Login & Logout
        // ==========================================
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVM vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // Verify CAPTCHA
            string? sessionCaptcha = HttpContext.Session.GetString("CaptchaCode");
            if (string.IsNullOrEmpty(vm.Captcha) || !string.Equals(vm.Captcha.Trim(), sessionCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.Captcha), "Invalid verification code. Please enter the code shown in the image.");
                return View(vm);
            }

            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == vm.Email.Trim().ToLower());
            if (user == null)
            {
                _helper.LogAudit(_db, null, vm.Email, "FailedLogin", "Non-existent user email attempt.");
                ModelState.AddModelError("", "Invalid email or password.");
                return View(vm);
            }

            // Check 3-Strike Lockout
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.Now)
            {
                var remainingMinutes = Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes);
                _helper.LogAudit(_db, user.Id, user.Email, "LockoutBlocked", $"Login blocked due to active lockout ({remainingMinutes} mins left).");
                ModelState.AddModelError("", $"Account is temporarily locked due to 3 failed login attempts. Please try again in {remainingMinutes} minute(s).");
                return View(vm);
            }

            // Verify Password
            if (!_helper.VerifyPassword(user.PasswordHash, vm.Password))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 3)
                {
                    user.LockoutEnd = DateTime.Now.AddMinutes(10);
                    _db.SaveChanges();
                    _helper.LogAudit(_db, user.Id, user.Email, "AccountLocked", "Account locked for 10 minutes following 3 consecutive failed login attempts.");
                    ModelState.AddModelError("", "Too many failed attempts! Your account has been temporarily locked for 10 minutes.");
                }
                else
                {
                    _db.SaveChanges();
                    int attemptsLeft = 3 - user.FailedLoginAttempts;
                    _helper.LogAudit(_db, user.Id, user.Email, "FailedLogin", $"Incorrect password attempt ({user.FailedLoginAttempts}/3).");
                    ModelState.AddModelError("", $"Invalid email or password. You have {attemptsLeft} attempt(s) remaining before account lockout.");
                }
                return View(vm);
            }

            // Successful Login: Reset lockout counters
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            _db.SaveChanges();

            // Check Email Verification
            if (!user.IsEmailVerified)
            {
                _helper.LogAudit(_db, user.Id, user.Email, "LoginBlockedUnverified", "Login blocked: email not verified.");
                TempData["ErrorMessage"] = "Your account is not activated yet. Please enter your 6-digit verification code below.";
                return RedirectToAction("VerifyEmail", new { email = user.Email });
            }

            // Set user preferences in session / cookie
            HttpContext.Session.SetString("UserTheme", user.ThemePreference ?? "light");
            HttpContext.Session.SetString("UserCurrency", user.PreferredCurrency ?? "MYR");

            await _helper.SignInAsync(user.Id, user.Email, user.FullName, user.Role, vm.RememberMe);
            _helper.LogAudit(_db, user.Id, user.Email, "LoginSuccess", $"User logged in with role: {user.Role}.");

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            TempData["SuccessMessage"] = $"Welcome back, {user.FullName}!";
            return RedirectToAction("Catalog", "Property");
        }

        [Authorize]
        public async Task<IActionResult> Logout()
        {
            string email = User.Identity?.Name ?? "Unknown";
            string idStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
            int.TryParse(idStr, out int userId);

            await _helper.SignOutAsync();
            _helper.LogAudit(_db, userId, email, "Logout", "User logged out.");
            TempData["SuccessMessage"] = "You have been signed out successfully.";
            return RedirectToAction("Catalog", "Property");
        }

        // ==========================================
        // 1.1 Registration & Email Activation
        // ==========================================
        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterVM vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // Verify CAPTCHA
            string? sessionCaptcha = HttpContext.Session.GetString("CaptchaCode");
            if (string.IsNullOrEmpty(vm.Captcha) || !string.Equals(vm.Captcha.Trim(), sessionCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.Captcha), "Invalid verification code.");
                return View(vm);
            }

            if (_db.Users.Any(u => u.Email.ToLower() == vm.Email.Trim().ToLower()))
            {
                ModelState.AddModelError(nameof(vm.Email), "Email is already registered.");
                return View(vm);
            }

            string otp = Random.Shared.Next(100000, 999999).ToString();
            string token = Guid.NewGuid().ToString("N");

            var newUser = new User
            {
                Email = vm.Email.Trim().ToLower(),
                PasswordHash = _helper.HashPassword(vm.Password),
                FullName = vm.FullName.Trim(),
                PhoneNumber = vm.PhoneNumber.Trim(),
                Role = vm.Role,
                IsEmailVerified = false,
                EmailVerificationToken = $"{token}:{otp}",
                CreatedAt = DateTime.Now
            };

            _db.Users.Add(newUser);
            _db.SaveChanges();

            _helper.LogAudit(_db, newUser.Id, newUser.Email, "UserRegistered", $"New user registered as {newUser.Role}. Email OTP code: {otp}.");

            // Dispatch Email via Live Gmail SMTP
            string verifyUrl = Url.Action("VerifyEmail", "Account", new { token }, Request.Scheme) ?? "";
            await _emailService.SendVerificationCodeAsync(newUser.Email, otp, newUser.FullName, verifyUrl);

            TempData["ActivationSimulationLink"] = verifyUrl;
            TempData["OtpCode"] = otp;
            TempData["SuccessMessage"] = $"Registration successful! A 6-digit verification code has been dispatched to {newUser.Email}.";

            return RedirectToAction("VerifyEmail", new { email = newUser.Email });
        }

        [HttpGet]
        public IActionResult VerifyEmail(string? token, string? email)
        {
            if (!string.IsNullOrEmpty(token))
            {
                var user = _db.Users.FirstOrDefault(u => u.EmailVerificationToken != null && u.EmailVerificationToken.Contains(token));
                if (user != null)
                {
                    user.IsEmailVerified = true;
                    user.EmailVerificationToken = null;
                    _db.SaveChanges();

                    _helper.LogAudit(_db, user.Id, user.Email, "EmailActivated", "User successfully verified email address via link.");
                    TempData["SuccessMessage"] = "Your email has been verified successfully! You can now log in.";
                    return RedirectToAction("Login");
                }
                TempData["ErrorMessage"] = "Invalid or expired account activation token.";
            }

            return View(new VerifyEmailVM { Email = email ?? "" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VerifyEmail(VerifyEmailVM vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == vm.Email.Trim().ToLower());
            if (user == null)
            {
                ModelState.AddModelError(nameof(vm.Email), "No account registered with this email.");
                return View(vm);
            }

            if (user.IsEmailVerified)
            {
                TempData["SuccessMessage"] = "This email is already verified. You can log in.";
                return RedirectToAction("Login");
            }

            // Verify OTP code
            bool isValid = false;
            if (!string.IsNullOrEmpty(user.EmailVerificationToken))
            {
                var parts = user.EmailVerificationToken.Split(':');
                string expectedOtp = parts.Length > 1 ? parts[1] : parts[0];
                if (expectedOtp == vm.OtpCode.Trim() || user.EmailVerificationToken == vm.OtpCode.Trim())
                {
                    isValid = true;
                }
            }

            if (!isValid)
            {
                ModelState.AddModelError(nameof(vm.OtpCode), "Invalid 6-digit verification code. Please check your email or resend a new code.");
                return View(vm);
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            _db.SaveChanges();

            _helper.LogAudit(_db, user.Id, user.Email, "EmailActivated", "User successfully verified email address via 6-digit OTP code.");
            TempData["SuccessMessage"] = "Your email has been verified successfully! You can now log in.";
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Please provide an email address.";
                return RedirectToAction("VerifyEmail");
            }

            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == email.Trim().ToLower());
            if (user != null && !user.IsEmailVerified)
            {
                string otp = Random.Shared.Next(100000, 999999).ToString();
                string token = Guid.NewGuid().ToString("N");
                user.EmailVerificationToken = $"{token}:{otp}";
                _db.SaveChanges();

                string verifyUrl = Url.Action("VerifyEmail", "Account", new { token }, Request.Scheme) ?? "";
                await _emailService.SendVerificationCodeAsync(user.Email, otp, user.FullName, verifyUrl);

                TempData["ActivationSimulationLink"] = verifyUrl;
                TempData["OtpCode"] = otp;
                TempData["SuccessMessage"] = $"A fresh 6-digit verification code was sent to {user.Email}.";
            }
            else
            {
                TempData["SuccessMessage"] = "If an unverified account exists for this email, a new code has been sent.";
            }

            return RedirectToAction("VerifyEmail", new { email });
        }

        // ==========================================
        // 1.1 Password Reset Flow
        // ==========================================
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordVM vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // Verify CAPTCHA
            string? sessionCaptcha = HttpContext.Session.GetString("CaptchaCode");
            if (string.IsNullOrEmpty(vm.Captcha) || !string.Equals(vm.Captcha.Trim(), sessionCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.Captcha), "Invalid verification code.");
                return View(vm);
            }

            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == vm.Email.Trim().ToLower());
            if (user != null)
            {
                string token = Guid.NewGuid().ToString("N");
                user.ResetToken = token;
                user.ResetTokenExpiry = DateTime.Now.AddHours(1);
                _db.SaveChanges();

                _helper.LogAudit(_db, user.Id, user.Email, "PasswordResetRequested", "Password reset token generated.");

                string resetUrl = Url.Action("ResetPassword", "Account", new { token, email = user.Email }, Request.Scheme) ?? "";
                
                // Dispatch Live Password Reset Email
                await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, resetUrl);

                TempData["ResetSimulationLink"] = resetUrl;
                TempData["SuccessMessage"] = "Password reset instructions sent! Please check your email inbox.";
            }
            else
            {
                // Prevent email enumeration
                TempData["SuccessMessage"] = "If an account with that email exists, reset instructions have been dispatched.";
            }

            return View(vm);
        }

        [HttpGet]
        public IActionResult ResetPassword(string token, string email)
        {
            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == email.Trim().ToLower() && u.ResetToken == token);
            if (user == null || user.ResetTokenExpiry < DateTime.Now)
            {
                TempData["ErrorMessage"] = "The password reset token is invalid or has expired.";
                return RedirectToAction("ForgotPassword");
            }

            return View(new ResetPasswordVM { Token = token, Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetPassword(ResetPasswordVM vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            var user = _db.Users.FirstOrDefault(u => u.Email.ToLower() == vm.Email.Trim().ToLower() && u.ResetToken == vm.Token);
            if (user == null || user.ResetTokenExpiry < DateTime.Now)
            {
                TempData["ErrorMessage"] = "The password reset link is invalid or expired.";
                return RedirectToAction("ForgotPassword");
            }

            user.PasswordHash = _helper.HashPassword(vm.NewPassword);
            user.ResetToken = null;
            user.ResetTokenExpiry = null;
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            _db.SaveChanges();

            _helper.LogAudit(_db, user.Id, user.Email, "PasswordResetCompleted", "Password successfully reset via token.");
            TempData["SuccessMessage"] = "Your password has been reset successfully. Please log in with your new password.";
            return RedirectToAction("Login");
        }

        // ==========================================
        // 1.2 Profile & Webcam Photo Management
        // ==========================================
        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            var vm = new ProfileVM
            {
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                Role = user.Role,
                CurrentPhoto = user.ProfilePhoto,
                ThemePreference = user.ThemePreference ?? "light",
                PreferredCurrency = user.PreferredCurrency ?? "MYR",
                BankName = user.BankName,
                BankAccountNumber = _helper.Decrypt(user.BankAccountNumberEncrypted ?? ""),
                BankHolderName = user.BankHolderName,
                IdentityStatus = user.IdentityStatus ?? "Unverified",
                IdDocumentPhoto = user.IdDocumentPhoto,
                IdentityNotes = user.IdentityNotes
            };

            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Profile(ProfileVM vm)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            if (!ModelState.IsValid)
            {
                vm.Email = user.Email;
                vm.Role = user.Role;
                vm.CurrentPhoto = user.ProfilePhoto;
                vm.IdentityStatus = user.IdentityStatus;
                vm.IdDocumentPhoto = user.IdDocumentPhoto;
                return View(vm);
            }

            user.FullName = vm.FullName.Trim();
            user.PhoneNumber = vm.PhoneNumber.Trim();
            user.ThemePreference = vm.ThemePreference;
            user.PreferredCurrency = vm.PreferredCurrency;

            // Bank details encryption
            if (!string.IsNullOrWhiteSpace(vm.BankAccountNumber))
            {
                user.BankName = vm.BankName?.Trim();
                user.BankAccountNumberEncrypted = _helper.Encrypt(vm.BankAccountNumber.Trim());
                user.BankHolderName = vm.BankHolderName?.Trim();
            }

            // Live Webcam Upload or File Upload
            if (!string.IsNullOrEmpty(vm.WebcamPhotoData))
            {
                try
                {
                    string webcamFile = _helper.SaveBase64Photo(vm.WebcamPhotoData, "profiles");
                    user.ProfilePhoto = webcamFile;
                }
                catch
                {
                    // Fallback
                }
            }
            else if (vm.PhotoUpload != null)
            {
                var err = _helper.ValidatePhoto(vm.PhotoUpload);
                if (err != null)
                {
                    ModelState.AddModelError(nameof(vm.PhotoUpload), err);
                    vm.Email = user.Email;
                    vm.Role = user.Role;
                    vm.CurrentPhoto = user.ProfilePhoto;
                    return View(vm);
                }
                user.ProfilePhoto = _helper.SavePhoto(vm.PhotoUpload, "profiles");
            }

            // Password change check
            if (!string.IsNullOrEmpty(vm.NewPassword))
            {
                if (string.IsNullOrEmpty(vm.CurrentPassword) || !_helper.VerifyPassword(user.PasswordHash, vm.CurrentPassword))
                {
                    ModelState.AddModelError(nameof(vm.CurrentPassword), "Current password is incorrect.");
                    vm.Email = user.Email;
                    vm.Role = user.Role;
                    vm.CurrentPhoto = user.ProfilePhoto;
                    return View(vm);
                }

                user.PasswordHash = _helper.HashPassword(vm.NewPassword);
            }

            _db.SaveChanges();
            _helper.LogAudit(_db, user.Id, user.Email, "ProfileUpdated", "User profile and preferences updated.");

            HttpContext.Session.SetString("UserTheme", user.ThemePreference);
            HttpContext.Session.SetString("UserCurrency", user.PreferredCurrency);

            TempData["SuccessMessage"] = "Profile settings updated successfully!";
            return RedirectToAction("Profile");
        }

        // ==========================================
        // 1.2 Host Identity Verification Submission
        // ==========================================
        [Authorize(Roles = "Host")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SubmitHostVerification(HostVerificationSubmissionVM vm)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            if (vm.IdDocument == null)
            {
                TempData["ErrorMessage"] = "Please select an identity document image to upload.";
                return RedirectToAction("Profile");
            }

            var err = _helper.ValidatePhoto(vm.IdDocument);
            if (err != null)
            {
                TempData["ErrorMessage"] = err;
                return RedirectToAction("Profile");
            }

            string docFile = _helper.SavePhoto(vm.IdDocument, "documents");
            user.IdDocumentPhoto = docFile;
            user.IdentityStatus = "PendingApproval";
            user.IdentityNotes = vm.Notes;
            _db.SaveChanges();

            _helper.LogAudit(_db, user.Id, user.Email, "HostVerificationSubmitted", "Host submitted ID document for admin approval.");
            TempData["SuccessMessage"] = "Identity document submitted for Admin approval. Verification status is now Pending.";
            return RedirectToAction("Profile");
        }

        // ==========================================
        // 1.2 & 7.2 Admin Approval of Host Identity
        // ==========================================
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveHostVerification(int userId, bool approve, string? notes)
        {
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            user.IdentityStatus = approve ? "Verified" : "Rejected";
            user.IdentityNotes = notes;
            user.IdentityApprovedAt = DateTime.Now;
            _db.SaveChanges();

            string adminEmail = User.Identity?.Name ?? "Admin";
            int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            _helper.LogAudit(_db, adminId, adminEmail, approve ? "HostVerified" : "HostRejected",
                $"Admin {(approve ? "verified" : "rejected")} Host #{user.Id} ({user.FullName}). Notes: {notes}");

            TempData["SuccessMessage"] = $"Host #{user.Id} identity status set to {user.IdentityStatus}.";
            return RedirectToAction("Users");
        }

        // ==========================================
        // 7.2 Admin User Management & Host Queue
        // ==========================================
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Users()
        {
            var users = _db.Users
                .Include(u => u.HostedProperties)
                .OrderByDescending(u => u.CreatedAt)
                .ToList();

            return View(users);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int userId)
        {
            var user = _db.Users
                .Include(u => u.HostedProperties)
                .Include(u => u.Bookings)
                .FirstOrDefault(u => u.Id == userId);

            if (user == null) return NotFound();

            if (user.Role == "Admin" && user.Id == 1)
            {
                TempData["ErrorMessage"] = "The primary System Administrator account cannot be deleted.";
                return RedirectToAction("Users");
            }

            var auditLogs = _db.AuditLogs.Where(a => a.UserId == userId).ToList();
            foreach (var log in auditLogs) log.UserId = null;

            _db.Users.Remove(user);
            _db.SaveChanges();

            int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            string adminEmail = User.Identity?.Name ?? "Admin";
            _helper.LogAudit(_db, adminId, adminEmail, "UserDeleted", $"Admin deleted user #{userId} ({user.FullName} - {user.Email}).");

            TempData["SuccessMessage"] = $"User {user.FullName} (#{userId}) has been deleted successfully.";
            return RedirectToAction("Users");
        }

        // ==========================================
        // 8.1 System Audit Trail Log Viewer
        // ==========================================
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult AuditLogs(string? search = null, string? actionType = null)
        {
            var query = _db.AuditLogs.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(a => a.UserEmail.Contains(search) || a.Details.Contains(search) || (a.IpAddress != null && a.IpAddress.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(actionType) && actionType != "All")
            {
                query = query.Where(a => a.Action == actionType);
            }

            var logs = query.OrderByDescending(a => a.Timestamp).Take(200).ToList();
            ViewBag.Search = search;
            ViewBag.ActionType = actionType;
            return View(logs);
        }

        // ==========================================
        // Demo Role Quick-Switcher
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> SwitchRole(string role)
        {
            // Normalize
            string targetRole = role switch
            {
                "Admin" => "Admin",
                "Host" or "Owner" => "Host",
                _ => "Guest"
            };

            var user = _db.Users.FirstOrDefault(u => u.Role == targetRole);
            if (user == null)
            {
                // Fallback to first user
                user = _db.Users.FirstOrDefault();
            }

            if (user != null)
            {
                await _helper.SignInAsync(user.Id, user.Email, user.FullName, user.Role, true);
                TempData["SuccessMessage"] = $"DEMO MODE: Switched to {user.Role} ({user.FullName})";
            }

            return RedirectToAction("Catalog", "Property");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
