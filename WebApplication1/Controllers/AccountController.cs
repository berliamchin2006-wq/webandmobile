using HomeNest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace HomeNest.Controllers
{
    public class AccountController : Controller
    {
        private readonly DB _db;
        private readonly Helper _helper;

        public AccountController(DB db, Helper helper)
        {
            _db = db;
            _helper = helper;
        }

        [AcceptVerbs("GET", "POST")]
        public IActionResult CheckEmail(string email)
        {
            bool exists = _db.Users.Any(u => u.Email == email);
            return Json(!exists);
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginVM());
        }

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVM vm, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // Verify CAPTCHA code from session
            string? sessionCaptcha = HttpContext.Session.GetString("CaptchaCode");
            if (string.IsNullOrEmpty(vm.Captcha) || !string.Equals(vm.Captcha.Trim(), sessionCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.Captcha), "Invalid verification code. Please enter the code shown in the image.");
                return View(vm);
            }

            var user = _db.Users.FirstOrDefault(u => u.Email == vm.Email);
            if (user == null)
            {
                ModelState.AddModelError("", "Invalid email or password.");
                return View(vm);
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.Now)
            {
                var remainingMinutes = Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes);
                ModelState.AddModelError("", $"Account is temporarily locked due to multiple failed login attempts. Please try again in {remainingMinutes} minute(s).");
                return View(vm);
            }

            if (!_helper.VerifyPassword(user.PasswordHash, vm.Password))
            {
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= 3)
                {
                    user.LockoutEnd = DateTime.Now.AddMinutes(15);
                    _db.SaveChanges();
                    ModelState.AddModelError("", "You have entered the wrong password 3 times. Your account is locked for 15 minutes.");
                    return View(vm);
                }

                _db.SaveChanges();
                int attemptsLeft = 3 - user.FailedLoginAttempts;
                ModelState.AddModelError("", $"Invalid email or password. ({attemptsLeft} attempt(s) remaining before temporary lockout).");
                return View(vm);
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            _db.SaveChanges();

            await _helper.SignInAsync(user.Id, user.Email, user.FullName, user.Role, vm.RememberMe);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return user.Role switch
            {
                "Admin" => RedirectToAction("Dashboard", "Booking"),
                "Owner" => RedirectToAction("MyProperties", "Property"),
                _ => RedirectToAction("Catalog", "Property")
            };
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterVM vm)
        {
            if (_db.Users.Any(u => u.Email == vm.Email))
            {
                ModelState.AddModelError(nameof(vm.Email), "Email is already registered.");
            }

            if (!ModelState.IsValid)
                return View(vm);

            var user = new User
            {
                Email = vm.Email.Trim(),
                FullName = vm.FullName.Trim(),
                PhoneNumber = vm.PhoneNumber.Trim(),
                Role = vm.Role,
                PasswordHash = _helper.HashPassword(vm.Password),
                IsEmailVerified = true
            };

            _db.Users.Add(user);
            _db.SaveChanges();

            await _helper.SignInAsync(user.Id, user.Email, user.FullName, user.Role, false);
            TempData["SuccessMessage"] = $"Welcome to HomeNest, {user.FullName}!";

            return RedirectToAction("Catalog", "Property");
        }

        public async Task<IActionResult> Logout()
        {
            await _helper.SignOutAsync();
            return RedirectToAction("Catalog", "Property");
        }

        [HttpGet]
        public async Task<IActionResult> QuickLogin(string role, string? returnUrl = null)
        {
            var user = _db.Users.FirstOrDefault(u => u.Role == role);
            if (user != null)
            {
                await _helper.SignInAsync(user.Id, user.Email, user.FullName, user.Role, false);
                TempData["SuccessMessage"] = $"Switched role to {user.Role} ({user.FullName}).";
            }
            return LocalRedirect(returnUrl ?? Url.Action("Catalog", "Property")!);
        }

        [HttpGet]
        public async Task<IActionResult> SwitchGuest(string? returnUrl = null)
        {
            await _helper.SignOutAsync();
            TempData["SuccessMessage"] = "Switched to Guest mode.";
            return LocalRedirect(returnUrl ?? Url.Action("Catalog", "Property")!);
        }

        [HttpGet]
        public IActionResult ResetData(string? returnUrl = null)
        {
            _db.MaintenanceTickets.RemoveRange(_db.MaintenanceTickets);
            _db.RentalPayments.RemoveRange(_db.RentalPayments);
            _db.TenancyBookings.RemoveRange(_db.TenancyBookings);
            _db.ViewingAppointments.RemoveRange(_db.ViewingAppointments);
            _db.RoomUnits.RemoveRange(_db.RoomUnits);
            _db.PropertyPhotos.RemoveRange(_db.PropertyPhotos);
            _db.Properties.RemoveRange(_db.Properties);
            _db.Users.RemoveRange(_db.Users);
            _db.SaveChanges();

            Program.SeedSampleData(_db, _helper);
            TempData["SuccessMessage"] = "Demo data has been reset to default state.";
            return LocalRedirect(returnUrl ?? Url.Action("Catalog", "Property")!);
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        // ==========================================
        // MODULE 1: USER MANAGEMENT
        // ==========================================

        [Authorize]
        [HttpGet]
        public IActionResult Profile()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            return View(user);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(string fullName, string phoneNumber, string? currentPassword, string? newPassword)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = _db.Users.Find(userId);
            if (user == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                user.FullName = fullName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                user.PhoneNumber = phoneNumber.Trim();
            }

            // Optional password update
            if (!string.IsNullOrWhiteSpace(currentPassword) && !string.IsNullOrWhiteSpace(newPassword))
            {
                if (!_helper.VerifyPassword(user.PasswordHash, currentPassword))
                {
                    TempData["ErrorMessage"] = "Current password is incorrect.";
                    return View(user);
                }

                if (newPassword.Length < 8)
                {
                    TempData["ErrorMessage"] = "New password must be at least 8 characters.";
                    return View(user);
                }

                user.PasswordHash = _helper.HashPassword(newPassword);
            }

            _db.SaveChanges();

            // Refresh cookie with updated name
            await _helper.SignInAsync(user.Id, user.Email, user.FullName, user.Role, false);

            TempData["SuccessMessage"] = "Profile details updated successfully.";
            return View(user);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult Users()
        {
            var list = _db.Users.OrderByDescending(u => u.CreatedAt).ToList();
            return View(list);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UnlockUser(int id)
        {
            var user = _db.Users.Find(id);
            if (user != null)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
                _db.SaveChanges();
                TempData["SuccessMessage"] = $"Account for {user.FullName} has been unlocked.";
            }
            return RedirectToAction("Users");
        }
    }
}