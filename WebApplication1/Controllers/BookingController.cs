using HomeNest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HomeNest.Controllers
{
    [Authorize]
    public class BookingController : Controller
    {
        private readonly DB _db;
        private readonly Helper _helper;

        public BookingController(DB db, Helper helper)
        {
            _db = db;
            _helper = helper;
        }

        // ==========================================
        // 1. VIEWING PASSES MODULE (Student 3)
        // ==========================================

        [HttpGet]
        public IActionResult ViewingPasses()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isTenant = User.IsInRole("Tenant");

            IQueryable<ViewingAppointment> query = _db.ViewingAppointments
                .Include(v => v.RoomUnit)
                .ThenInclude(r => r.Property)
                .ThenInclude(p => p.Owner)
                .Include(v => v.Tenant);

            if (isTenant)
            {
                query = query.Where(v => v.TenantId == userId);
            }
            else
            {
                query = query.Where(v => v.RoomUnit.Property.OwnerId == userId || User.IsInRole("Admin"));
            }

            var list = query.OrderByDescending(v => v.AppointmentDateTime).ToList();
            return View(list);
        }

        [Authorize(Roles = "Tenant")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ScheduleViewing(int roomUnitId, DateTime appointmentDateTime, string? remarks)
        {
            var room = _db.RoomUnits.Include(r => r.Property).FirstOrDefault(r => r.Id == roomUnitId);
            if (room == null) return NotFound();

            if (appointmentDateTime < DateTime.Now.AddHours(2))
            {
                TempData["ErrorMessage"] = "Viewing appointment must be at least 2 hours in advance.";
                return RedirectToAction("Details", "Property", new { id = room.PropertyId });
            }

            int tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var pass = new ViewingAppointment
            {
                RoomUnitId = roomUnitId,
                TenantId = tenantId,
                AppointmentDateTime = appointmentDateTime,
                Remarks = remarks,
                Status = "Confirmed",
                QrCodeToken = "PASS-" + Guid.NewGuid().ToString("N")[..8].ToUpper()
            };

            _db.ViewingAppointments.Add(pass);
            _db.SaveChanges();

            TempData["SuccessMessage"] = $"QR Viewing Pass generated successfully! Reference: {pass.QrCodeToken}";
            return RedirectToAction("ViewingPasses");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateViewingStatus(int id, string status)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var pass = _db.ViewingAppointments
                .Include(v => v.RoomUnit)
                .ThenInclude(r => r.Property)
                .FirstOrDefault(v => v.Id == id);

            if (pass == null) return NotFound();

            if (pass.TenantId == userId || pass.RoomUnit.Property.OwnerId == userId || User.IsInRole("Admin"))
            {
                pass.Status = status;
                _db.SaveChanges();
                TempData["SuccessMessage"] = $"Viewing pass status updated to {status}.";
            }

            return RedirectToAction("ViewingPasses");
        }

        // ==========================================
        // 2. LEASES & BILLING MODULE (Student 3 & 4)
        // ==========================================

        [Authorize(Roles = "Tenant")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApplyLease(TenancyBookingVM vm)
        {
            var room = _db.RoomUnits
                .Include(r => r.Property)
                .FirstOrDefault(r => r.Id == vm.RoomUnitId);

            if (room == null) return NotFound();

            if (vm.StartDate.Date < DateTime.Today)
            {
                ModelState.AddModelError(nameof(vm.StartDate), "Move-in date cannot be in the past.");
            }

            DateTime calculatedEndDate = vm.StartDate.AddMonths(vm.DurationMonths);

            bool hasOverlap = _db.TenancyBookings.Any(b =>
                b.RoomUnitId == vm.RoomUnitId &&
                (b.Status == "Active" || b.Status == "PendingApproval") &&
                vm.StartDate < b.EndDate &&
                calculatedEndDate > b.StartDate);

            if (hasOverlap)
            {
                ModelState.AddModelError("", "This room already has an active or pending lease overlapping with your selected dates.");
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction("Details", "Property", new { id = room.PropertyId });
            }

            int tenantId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            decimal totalDeposit = room.SecurityDeposit + room.UtilityDeposit + room.MonthlyRent;

            var booking = new TenancyBooking
            {
                RoomUnitId = room.Id,
                TenantId = tenantId,
                StartDate = vm.StartDate,
                EndDate = calculatedEndDate,
                DurationMonths = vm.DurationMonths,
                AgreedMonthlyRent = room.MonthlyRent,
                TotalDepositPaid = totalDeposit,
                Status = "PendingApproval"
            };

            _db.TenancyBookings.Add(booking);
            _db.SaveChanges();

            TempData["SuccessMessage"] = "Tenancy application submitted! Once approved by the owner, you can pay the initial deposit.";
            return RedirectToAction("Leases");
        }

        [HttpGet]
        public IActionResult Leases()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isTenant = User.IsInRole("Tenant");

            IQueryable<TenancyBooking> query = _db.TenancyBookings
                .Include(b => b.RoomUnit)
                .ThenInclude(r => r.Property)
                .ThenInclude(p => p.Owner)
                .Include(b => b.Tenant)
                .Include(b => b.Payments);

            if (isTenant)
            {
                query = query.Where(b => b.TenantId == userId);
            }
            else
            {
                query = query.Where(b => b.RoomUnit.Property.OwnerId == userId || User.IsInRole("Admin"));
            }

            var list = query.OrderByDescending(b => b.BookingDate).ToList();
            return View(list);
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveLease(int id)
        {
            var booking = _db.TenancyBookings.Find(id);
            if (booking != null)
            {
                booking.Status = "ApprovedWaitingPayment";
                _db.SaveChanges();
                TempData["SuccessMessage"] = "Lease application approved! Tenant can now pay the initial deposit.";
            }
            return RedirectToAction("Leases");
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RejectLease(int id)
        {
            var booking = _db.TenancyBookings.Find(id);
            if (booking != null)
            {
                booking.Status = "Rejected";
                _db.SaveChanges();
                TempData["SuccessMessage"] = "Lease application declined.";
            }
            return RedirectToAction("Leases");
        }

        [Authorize(Roles = "Tenant")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult PayDeposit(int bookingId, string paymentMethod)
        {
            var booking = _db.TenancyBookings
                .Include(b => b.RoomUnit)
                .FirstOrDefault(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            var payment = new RentalPayment
            {
                TenancyBookingId = booking.Id,
                PaymentType = "InitialDepositAnd1stMonthRent",
                BillingMonth = DateTime.Now.ToString("yyyy-MM"),
                Amount = booking.TotalDepositPaid,
                PaymentDate = DateTime.Now,
                PaymentMethod = paymentMethod ?? "Stripe / FPX Online Banking",
                TransactionRef = "TXN-" + Guid.NewGuid().ToString("N")[..10].ToUpper(),
                Status = "Paid"
            };

            _db.RentalPayments.Add(payment);
            booking.Status = "Active";
            booking.RoomUnit.IsAvailable = false;
            _db.SaveChanges();

            TempData["SuccessMessage"] = $"Payment successful! Transaction Ref: {payment.TransactionRef}";
            return RedirectToAction("Receipt", new { id = payment.Id });
        }

        [HttpGet]
        public IActionResult Receipt(int id)
        {
            var payment = _db.RentalPayments
                .Include(p => p.TenancyBooking)
                .ThenInclude(b => b.RoomUnit)
                .ThenInclude(r => r.Property)
                .ThenInclude(p => p.Owner)
                .Include(p => p.TenancyBooking.Tenant)
                .FirstOrDefault(p => p.Id == id);

            if (payment == null) return NotFound();
            return View(payment);
        }

        // Redirect old MyBookings route to Leases
        [HttpGet]
        public IActionResult MyBookings() => RedirectToAction("Leases");

        // ==========================================
        // 3. MAINTENANCE MODULE (Student 4)
        // ==========================================

        [HttpGet]
        public IActionResult Maintenance()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isTenant = User.IsInRole("Tenant");

            IQueryable<MaintenanceTicket> query = _db.MaintenanceTickets
                .Include(t => t.TenancyBooking)
                .ThenInclude(b => b.RoomUnit)
                .ThenInclude(r => r.Property)
                .ThenInclude(p => p.Owner)
                .Include(t => t.TenancyBooking.Tenant);

            if (isTenant)
            {
                query = query.Where(t => t.TenancyBooking.TenantId == userId);
            }
            else
            {
                query = query.Where(t => t.TenancyBooking.RoomUnit.Property.OwnerId == userId || User.IsInRole("Admin"));
            }

            ViewBag.ActiveLeases = _db.TenancyBookings
                .Include(b => b.RoomUnit)
                .ThenInclude(r => r.Property)
                .Where(b => (isTenant ? b.TenantId == userId : b.RoomUnit.Property.OwnerId == userId) && (b.Status == "Active" || b.Status == "ApprovedWaitingPayment"))
                .ToList();

            var list = query.OrderByDescending(t => t.CreatedAt).ToList();
            return View(list);
        }

        [Authorize(Roles = "Tenant")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReportIssue(int tenancyBookingId, string issueTitle, string description, string priority, IFormFile? photo)
        {
            if (string.IsNullOrWhiteSpace(issueTitle) || string.IsNullOrWhiteSpace(description))
            {
                TempData["ErrorMessage"] = "Please fill in all required ticket details.";
                return RedirectToAction("Maintenance");
            }

            string? photoPath = null;
            if (photo != null && photo.Length > 0)
            {
                photoPath = _helper.SavePhoto(photo, "maintenance");
            }

            var ticket = new MaintenanceTicket
            {
                TenancyBookingId = tenancyBookingId,
                IssueTitle = issueTitle.Trim(),
                Description = description.Trim(),
                Priority = priority ?? "Medium",
                PhotoProof = photoPath,
                Status = "Reported",
                RepairCost = 0m,
                CreatedAt = DateTime.Now
            };

            _db.MaintenanceTickets.Add(ticket);
            _db.SaveChanges();

            TempData["SuccessMessage"] = $"Maintenance ticket #{ticket.Id} submitted. The landlord has been notified.";
            return RedirectToAction("Maintenance");
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateTicket(int id, string status, decimal repairCost)
        {
            var ticket = _db.MaintenanceTickets.Find(id);
            if (ticket != null)
            {
                ticket.Status = status;
                ticket.RepairCost = repairCost;
                _db.SaveChanges();
                TempData["SuccessMessage"] = $"Ticket #{ticket.Id} status updated to {status}.";
            }
            return RedirectToAction("Maintenance");
        }

        // ==========================================
        // 4. ANALYTICS & DASHBOARD (Student 4)
        // ==========================================

        [Authorize(Roles = "Owner,Admin")]
        [HttpGet]
        public IActionResult GetRevenueChartData()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var monthlyRevenue = _db.RentalPayments
                .Include(p => p.TenancyBooking)
                .ThenInclude(b => b.RoomUnit)
                .ThenInclude(r => r.Property)
                .Where(p => p.Status == "Paid" && (isAdmin || p.TenancyBooking.RoomUnit.Property.OwnerId == userId))
                .GroupBy(p => p.BillingMonth)
                .Select(g => new
                {
                    Month = g.Key,
                    TotalRevenue = g.Sum(x => x.Amount)
                })
                .OrderBy(g => g.Month)
                .ToList();

            return Json(monthlyRevenue);
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpGet]
        public IActionResult Dashboard()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var properties = _db.Properties
                .Include(p => p.RoomUnits)
                .Where(p => isAdmin || p.OwnerId == userId)
                .ToList();

            int totalRooms = properties.SelectMany(p => p.RoomUnits).Count();
            int occupiedRooms = properties.SelectMany(p => p.RoomUnits).Count(r => !r.IsAvailable);

            ViewBag.TotalProperties = properties.Count;
            ViewBag.TotalRooms = totalRooms;
            ViewBag.OccupiedRooms = occupiedRooms;
            ViewBag.OccupancyRate = totalRooms > 0 ? (int)((occupiedRooms / (double)totalRooms) * 100) : 0;
            ViewBag.TotalTickets = _db.MaintenanceTickets.Count(t => isAdmin || t.TenancyBooking.RoomUnit.Property.OwnerId == userId);

            return View();
        }
    }
}
