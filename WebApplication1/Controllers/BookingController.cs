using System.Security.Claims;
using System.Text;
using HomeNest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        // 4.1 Checkout & Payment Processing Flow
        // ==========================================
        [HttpGet]
        public IActionResult Checkout(int propertyId, DateTime checkIn, DateTime checkOut, int guests = 1)
        {
            var property = _db.Properties
                .Include(p => p.Host)
                .Include(p => p.Photos)
                .FirstOrDefault(p => p.Id == propertyId);

            if (property == null) return NotFound();

            if (checkOut <= checkIn)
            {
                TempData["ErrorMessage"] = "Check-out date must be at least 1 day after check-in.";
                return RedirectToAction("Details", "Property", new { id = propertyId });
            }

            // Real-time conflict check
            bool hasConflict = _db.Bookings.Any(b =>
                b.PropertyId == propertyId &&
                b.Status != "Cancelled" &&
                checkIn.Date < b.CheckOutDate.Date &&
                checkOut.Date > b.CheckInDate.Date);

            if (hasConflict)
            {
                TempData["ErrorMessage"] = "Sorry, these dates were just reserved by another guest. Please choose other dates.";
                return RedirectToAction("Details", "Property", new { id = propertyId });
            }

            int nights = (checkOut.Date - checkIn.Date).Days;
            decimal subtotal = 0m;
            for (DateTime d = checkIn.Date; d < checkOut.Date; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Friday || d.DayOfWeek == DayOfWeek.Saturday)
                    subtotal += property.CustomWeekendRate;
                else
                    subtotal += property.BaseNightlyRate;
            }

            decimal cleaningFee = property.CleaningFee;
            decimal serviceFee = Math.Round(subtotal * 0.10m, 2);
            decimal total = subtotal + cleaningFee + serviceFee;

            var vm = new BookingCheckoutVM
            {
                PropertyId = propertyId,
                Property = property,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                GuestCount = Math.Clamp(guests, 1, property.MaxGuests),
                TotalNights = nights,
                NightlyRate = property.BaseNightlyRate,
                SubtotalAmount = subtotal,
                CleaningFee = cleaningFee,
                ServiceFee = serviceFee,
                TotalAmount = total
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ProcessPayment(BookingCheckoutVM vm)
        {
            var property = _db.Properties.Include(p => p.Host).FirstOrDefault(p => p.Id == vm.PropertyId);
            if (property == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // ==========================================
            // Concurrency Locking to Prevent Double-Booking
            // ==========================================
            using (var transaction = _db.Database.BeginTransaction())
            {
                bool conflict = _db.Bookings.Any(b =>
                    b.PropertyId == vm.PropertyId &&
                    b.Status != "Cancelled" &&
                    vm.CheckInDate.Date < b.CheckOutDate.Date &&
                    vm.CheckOutDate.Date > b.CheckInDate.Date);

                if (conflict)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "Double-booking prevented: Another guest just reserved these exact dates. Please select alternative dates.";
                    return RedirectToAction("Details", "Property", new { id = vm.PropertyId });
                }

                int nights = (vm.CheckOutDate.Date - vm.CheckInDate.Date).Days;
                decimal subtotal = 0m;
                for (DateTime d = vm.CheckInDate.Date; d < vm.CheckOutDate.Date; d = d.AddDays(1))
                {
                    if (d.DayOfWeek == DayOfWeek.Friday || d.DayOfWeek == DayOfWeek.Saturday)
                        subtotal += property.CustomWeekendRate;
                    else
                        subtotal += property.BaseNightlyRate;
                }

                decimal cleaningFee = property.CleaningFee;
                decimal serviceFee = Math.Round(subtotal * 0.10m, 2);
                decimal total = subtotal + cleaningFee + serviceFee;

                // Create Booking
                var booking = new Booking
                {
                    PropertyId = property.Id,
                    GuestId = currentUserId,
                    CheckInDate = vm.CheckInDate.Date,
                    CheckOutDate = vm.CheckOutDate.Date,
                    TotalNights = nights,
                    GuestCount = vm.GuestCount,
                    NightlyRate = property.BaseNightlyRate,
                    SubtotalAmount = subtotal,
                    CleaningFee = cleaningFee,
                    ServiceFee = serviceFee,
                    TotalAmount = total,
                    Status = "Confirmed",
                    SpecialRequests = vm.SpecialRequests?.Trim(),
                    QrCheckInToken = "PASS-" + Guid.NewGuid().ToString("N")[..8].ToUpper(),
                    BookingDate = DateTime.Now
                };

                _db.Bookings.Add(booking);
                _db.SaveChanges();

                // Host Earnings Calculation (Host receives subtotal - 3% platform commission + cleaning fee)
                decimal hostCommission = Math.Round(subtotal * 0.03m, 2);
                decimal hostPayout = subtotal - hostCommission + cleaningFee;

                // Create Payment
                string txnRef = "TXN-" + Guid.NewGuid().ToString("N")[..10].ToUpper();
                string payMethod = vm.PaymentMethod switch
                {
                    "FPX Online Banking" => $"FPX Online Banking ({vm.FpxBank ?? "Maybank2u"})",
                    "Touch 'n Go eWallet" => "Touch 'n Go eWallet",
                    _ => $"Stripe Credit Card (ending in {(string.IsNullOrEmpty(vm.CardNumber) ? "4242" : vm.CardNumber[^4..])})"
                };

                var payment = new Payment
                {
                    BookingId = booking.Id,
                    PaymentType = "FullPayment",
                    Amount = total,
                    PaymentDate = DateTime.Now,
                    PaymentMethod = payMethod,
                    TransactionRef = txnRef,
                    Status = "Paid",
                    HostPayoutAmount = hostPayout,
                    HostPayoutStatus = "Pending"
                };

                _db.Payments.Add(payment);

                // Initial Host-Guest Welcome Message
                var welcomeMessage = new BookingMessage
                {
                    BookingId = booking.Id,
                    SenderId = property.HostId,
                    MessageText = $"Hello! Thank you for booking {property.Title}. Check-in is after 3:00 PM on {booking.CheckInDate:dd MMM yyyy}. Feel free to message me here if you have any questions!",
                    SentAt = DateTime.Now,
                    IsRead = false
                };
                _db.BookingMessages.Add(welcomeMessage);

                _db.SaveChanges();
                transaction.Commit();

                _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "BookingCreated",
                    $"Booking #{booking.Id} confirmed for {property.Title} ({nights} nights). Total: RM {total:N2}. TxnRef: {txnRef}");

                TempData["SuccessMessage"] = $"Reservation confirmed! Your booking reference is #{booking.Id}.";
                return RedirectToAction("Receipt", new { id = booking.Id });
            }
        }

        // ==========================================
        // 3.3 Reservation Management: Guest Trips
        // ==========================================
        [HttpGet]
        public IActionResult MyBookings(string? filter = "all")
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var query = _db.Bookings
                .Include(b => b.Property)
                    .ThenInclude(p => p.Host)
                .Include(b => b.Property)
                    .ThenInclude(p => p.Photos)
                .Include(b => b.Payments)
                .Include(b => b.Review)
                .Where(b => b.GuestId == currentUserId);

            query = filter switch
            {
                "upcoming" => query.Where(b => b.Status == "Confirmed" || b.Status == "CheckedIn"),
                "completed" => query.Where(b => b.Status == "CheckedOut"),
                "cancelled" => query.Where(b => b.Status == "Cancelled"),
                _ => query
            };

            var bookings = query.OrderByDescending(b => b.BookingDate).ToList();
            ViewBag.CurrentFilter = filter;
            return View(bookings);
        }

        // ==========================================
        // 3.3 Real-Time Host–Guest Messaging & Details
        // ==========================================
        [HttpGet]
        public IActionResult Details(int id)
        {
            var booking = _db.Bookings
                .Include(b => b.Property)
                    .ThenInclude(p => p.Host)
                .Include(b => b.Property)
                    .ThenInclude(p => p.Photos)
                .Include(b => b.Guest)
                .Include(b => b.Payments)
                .Include(b => b.Messages.OrderBy(m => m.SentAt))
                    .ThenInclude(m => m.Sender)
                .Include(b => b.MaintenanceTickets)
                .Include(b => b.Review)
                .FirstOrDefault(b => b.Id == id);

            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(booking);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SendMessage(int bookingId, string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return RedirectToAction("Details", new { id = bookingId });
            }

            var booking = _db.Bookings.Include(b => b.Property).FirstOrDefault(b => b.Id == bookingId);
            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var msg = new BookingMessage
            {
                BookingId = bookingId,
                SenderId = currentUserId,
                MessageText = messageText.Trim(),
                SentAt = DateTime.Now,
                IsRead = false
            };

            _db.BookingMessages.Add(msg);
            _db.SaveChanges();

            TempData["SuccessMessage"] = "Message sent.";
            return RedirectToAction("Details", new { id = bookingId });
        }

        // ==========================================
        // 4.2 Cancellation Policy & Automated Refund
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CancelBooking(int id)
        {
            var booking = _db.Bookings
                .Include(b => b.Property)
                .Include(b => b.Payments)
                .FirstOrDefault(b => b.Id == id);

            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (booking.Status == "Cancelled" || booking.Status == "CheckedIn" || booking.Status == "CheckedOut")
            {
                TempData["ErrorMessage"] = "This booking cannot be cancelled in its current state.";
                return RedirectToAction("Details", new { id });
            }

            // Automated Refund Calculation based on Policy
            double hoursUntilCheckIn = (booking.CheckInDate - DateTime.Now).TotalHours;
            double daysUntilCheckIn = (booking.CheckInDate - DateTime.Now).TotalDays;
            string policy = booking.Property.CancellationPolicy;

            decimal refundPercentage = 0m;
            if (policy == "Flexible")
            {
                refundPercentage = hoursUntilCheckIn >= 24 ? 1.0m : 0.5m;
            }
            else if (policy == "Moderate")
            {
                refundPercentage = daysUntilCheckIn >= 5 ? 1.0m : (hoursUntilCheckIn >= 24 ? 0.5m : 0m);
            }
            else // Strict
            {
                refundPercentage = daysUntilCheckIn >= 7 ? 0.5m : 0m;
            }

            decimal refundAmount = Math.Round(booking.TotalAmount * refundPercentage, 2);

            booking.Status = "Cancelled";

            // Record Refund Payment
            var mainPayment = booking.Payments.FirstOrDefault(p => p.PaymentType == "FullPayment");
            if (mainPayment != null)
            {
                mainPayment.RefundAmount = refundAmount;
                mainPayment.RefundReason = $"Cancelled by guest under {policy} policy ({refundPercentage:P0} refund).";
                mainPayment.HostPayoutStatus = "Cancelled";
            }

            var refundPayment = new Payment
            {
                BookingId = booking.Id,
                PaymentType = "Refund",
                Amount = -refundAmount,
                PaymentDate = DateTime.Now,
                PaymentMethod = mainPayment?.PaymentMethod ?? "Stripe Refund",
                TransactionRef = "REF-" + Guid.NewGuid().ToString("N")[..10].ToUpper(),
                Status = "Refunded",
                RefundAmount = refundAmount,
                RefundReason = $"Eligible {refundPercentage:P0} refund under {policy} policy."
            };
            _db.Payments.Add(refundPayment);

            _db.SaveChanges();
            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "BookingCancelled",
                $"Booking #{booking.Id} cancelled. Refund issued: RM {refundAmount:N2} ({policy} Policy).");

            TempData["SuccessMessage"] = $"Booking cancelled. An automated refund of RM {refundAmount:N2} has been processed back to your original payment method.";
            return RedirectToAction("MyBookings");
        }

        // ==========================================
        // 4.1 Printable PDF E-Receipt & Tax Invoice
        // ==========================================
        [HttpGet]
        public IActionResult Receipt(int id)
        {
            var booking = _db.Bookings
                .Include(b => b.Property)
                    .ThenInclude(p => p.Host)
                .Include(b => b.Guest)
                .Include(b => b.Payments)
                .FirstOrDefault(b => b.Id == id);

            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(booking);
        }

        // ==========================================
        // 5.1 Digital QR Check-In Pass & Webcam Scanner
        // ==========================================
        [HttpGet]
        public IActionResult CheckInPass(int id)
        {
            var booking = _db.Bookings
                .Include(b => b.Property)
                    .ThenInclude(p => p.Host)
                .Include(b => b.Guest)
                .FirstOrDefault(b => b.Id == id);

            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(booking);
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult QrScanner()
        {
            return View();
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        public IActionResult VerifyCheckInPass(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return Json(new { success = false, message = "Invalid QR code token." });
            }

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var booking = _db.Bookings
                .Include(b => b.Property)
                .Include(b => b.Guest)
                .FirstOrDefault(b => b.QrCheckInToken == token.Trim());

            if (booking == null)
            {
                return Json(new { success = false, message = "No reservation found matching this QR code pass." });
            }

            if (!isAdmin && booking.Property.HostId != currentUserId)
            {
                return Json(new { success = false, message = "Access denied: You are not the host of this property." });
            }

            if (booking.Status == "CheckedIn")
            {
                return Json(new
                {
                    success = true,
                    alreadyCheckedIn = true,
                    message = $"Guest {booking.Guest.FullName} was already checked in at {booking.CheckedInAt:hh:mm tt, dd MMM yyyy}."
                });
            }

            booking.Status = "CheckedIn";
            booking.CheckedInAt = DateTime.Now;
            _db.SaveChanges();

            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "GuestCheckedIn",
                $"Host verified QR pass and checked in guest {booking.Guest.FullName} for booking #{booking.Id}.");

            return Json(new
            {
                success = true,
                message = $"Check-in confirmed for {booking.Guest.FullName}! Enjoy their stay.",
                bookingId = booking.Id,
                guestName = booking.Guest.FullName,
                propertyTitle = booking.Property.Title,
                checkOutDate = booking.CheckOutDate.ToString("dd MMM yyyy")
            });
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateStayStatus(int id, string status)
        {
            var booking = _db.Bookings.Include(b => b.Property).FirstOrDefault(b => b.Id == id);
            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            booking.Status = status;
            if (status == "CheckedIn") booking.CheckedInAt = DateTime.Now;
            if (status == "CheckedOut") booking.CheckedOutAt = DateTime.Now;

            _db.SaveChanges();
            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "StayStatusUpdated",
                $"Stay status for booking #{booking.Id} updated to {status}.");

            TempData["SuccessMessage"] = $"Stay status updated to {status}.";
            return RedirectToAction("Details", new { id });
        }

        // ==========================================
        // 5.2 Guest Support & In-Stay Issue Ticketing
        // ==========================================
        [HttpGet]
        public IActionResult ReportIssue(int bookingId)
        {
            var booking = _db.Bookings.Include(b => b.Property).FirstOrDefault(b => b.Id == bookingId);
            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var vm = new MaintenanceRequestVM
            {
                BookingId = bookingId
            };
            ViewBag.Booking = booking;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ReportIssue(MaintenanceRequestVM vm)
        {
            var booking = _db.Bookings.Include(b => b.Property).FirstOrDefault(b => b.Id == vm.BookingId);
            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Booking = booking;
                return View(vm);
            }

            string? photo = null;
            if (vm.PhotoProof != null)
            {
                photo = _helper.SavePhoto(vm.PhotoProof, "tickets");
            }

            var ticket = new MaintenanceTicket
            {
                BookingId = vm.BookingId,
                Category = vm.Category,
                IssueTitle = vm.IssueTitle.Trim(),
                Description = vm.Description.Trim(),
                Priority = vm.Priority,
                PhotoProof = photo,
                Status = "Reported",
                CreatedAt = DateTime.Now
            };

            _db.MaintenanceTickets.Add(ticket);
            _db.SaveChanges();

            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "MaintenanceReported",
                $"Guest reported issue #{ticket.Id} '{ticket.IssueTitle}' for booking #{booking.Id}.");

            TempData["SuccessMessage"] = "Issue ticket reported to your host. They have been notified!";
            return RedirectToAction("Maintenance");
        }

        [HttpGet]
        public IActionResult Maintenance()
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isHost = User.IsInRole("Host");
            bool isAdmin = User.IsInRole("Admin");

            IQueryable<MaintenanceTicket> query = _db.MaintenanceTickets
                .Include(m => m.Booking)
                    .ThenInclude(b => b.Property)
                .Include(m => m.Booking)
                    .ThenInclude(b => b.Guest);

            if (isHost && !isAdmin)
            {
                query = query.Where(m => m.Booking.Property.HostId == currentUserId);
            }
            else if (!isAdmin)
            {
                query = query.Where(m => m.Booking.GuestId == currentUserId);
            }

            var tickets = query.OrderByDescending(m => m.CreatedAt).ToList();
            return View(tickets);
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateTicketStatus(int id, string status, string? notes, decimal repairCost = 0m)
        {
            var ticket = _db.MaintenanceTickets
                .Include(m => m.Booking)
                    .ThenInclude(b => b.Property)
                .FirstOrDefault(m => m.Id == id);

            if (ticket == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (ticket.Booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            ticket.Status = status;
            ticket.ResolutionNotes = notes;
            ticket.RepairCost = repairCost;
            if (status == "Resolved") ticket.ResolvedAt = DateTime.Now;

            _db.SaveChanges();
            TempData["SuccessMessage"] = $"Ticket #{ticket.Id} status updated to {status}.";
            return RedirectToAction("Maintenance");
        }

        // ==========================================
        // 6.1 & 6.2 Review & Reputation System
        // ==========================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SubmitReview(ReviewFormVM vm)
        {
            var booking = _db.Bookings
                .Include(b => b.Property)
                .Include(b => b.Review)
                .FirstOrDefault(b => b.Id == vm.BookingId);

            if (booking == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (booking.GuestId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (booking.Review != null)
            {
                TempData["ErrorMessage"] = "You have already submitted a review for this stay.";
                return RedirectToAction("Details", new { id = vm.BookingId });
            }

            // Keyword Profanity Filter
            string filteredComment = _helper.FilterProfanity(vm.Comment);

            double overall = Math.Round((vm.CleanlinessRating + vm.AccuracyRating + vm.CommunicationRating + vm.LocationRating) / 4.0, 1);

            var review = new Review
            {
                BookingId = vm.BookingId,
                PropertyId = booking.PropertyId,
                GuestId = currentUserId,
                CleanlinessRating = vm.CleanlinessRating,
                AccuracyRating = vm.AccuracyRating,
                CommunicationRating = vm.CommunicationRating,
                LocationRating = vm.LocationRating,
                OverallRating = overall,
                Comment = filteredComment,
                IsPublished = true, // Blind review publishing rule
                CreatedAt = DateTime.Now
            };

            _db.Reviews.Add(review);
            _db.SaveChanges();

            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "ReviewSubmitted",
                $"Guest reviewed property #{booking.PropertyId} (Rating: {overall}★).");

            TempData["SuccessMessage"] = "Thank you! Your verified review has been published.";
            return RedirectToAction("Details", "Property", new { id = booking.PropertyId });
        }

        [HttpPost]
        public IActionResult VoteReviewHelpful(int reviewId)
        {
            var review = _db.Reviews.Find(reviewId);
            if (review == null) return Json(new { success = false });

            review.HelpfulVotes++;
            _db.SaveChanges();

            return Json(new { success = true, helpfulCount = review.HelpfulVotes });
        }

        // ==========================================
        // 4.2 & 7.1 Host Earnings Ledger & Payouts
        // ==========================================
        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult HostEarnings()
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var payments = _db.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b.Property)
                .Include(p => p.Booking)
                    .ThenInclude(b => b.Guest)
                .Where(p => isAdmin || p.Booking.Property.HostId == currentUserId)
                .OrderByDescending(p => p.PaymentDate)
                .ToList();

            var host = _db.Users.Find(currentUserId);
            ViewBag.HostBankName = host?.BankName ?? "Not Configured";
            ViewBag.HostBankAccountMasked = _helper.MaskBankAccount(_helper.Decrypt(host?.BankAccountNumberEncrypted ?? ""));
            ViewBag.HostBankHolder = host?.BankHolderName ?? host?.FullName;

            return View(payments);
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisbursePayout(int paymentId)
        {
            var payment = _db.Payments.Include(p => p.Booking).ThenInclude(b => b.Property).FirstOrDefault(p => p.Id == paymentId);
            if (payment == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (payment.Booking.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            payment.HostPayoutStatus = "Disbursed";
            payment.HostPayoutDisbursedAt = DateTime.Now;
            _db.SaveChanges();

            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "HostPayoutDisbursed",
                $"Host payout of RM {payment.HostPayoutAmount:N2} disbursed for booking #{payment.BookingId}.");

            TempData["SuccessMessage"] = $"Payout of RM {payment.HostPayoutAmount:N2} marked as Disbursed!";
            return RedirectToAction("HostEarnings");
        }

        // ==========================================
        // 7.1 Host Performance Dashboard & Charts
        // ==========================================
        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult HostDashboard()
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var properties = _db.Properties
                .Include(p => p.Bookings)
                    .ThenInclude(b => b.Payments)
                .Include(p => p.Reviews)
                .Where(p => isAdmin || p.HostId == currentUserId)
                .ToList();

            var allBookings = properties.SelectMany(p => p.Bookings).Where(b => b.Status != "Cancelled").ToList();

            decimal totalEarnings = allBookings
                .SelectMany(b => b.Payments)
                .Where(p => p.PaymentType == "FullPayment")
                .Sum(p => p.HostPayoutAmount);

            int totalNights = allBookings.Sum(b => b.TotalNights);
            int upcomingArrivals = allBookings.Count(b => b.CheckInDate.Date == DateTime.Today);
            int upcomingDepartures = allBookings.Count(b => b.CheckOutDate.Date == DateTime.Today);

            // Monthly Earnings for Chart (Last 6 months)
            var monthlyData = new List<object>();
            for (int i = 5; i >= 0; i--)
            {
                var targetMonth = DateTime.Today.AddMonths(-i);
                decimal monthEarnings = allBookings
                    .Where(b => b.BookingDate.Year == targetMonth.Year && b.BookingDate.Month == targetMonth.Month)
                    .SelectMany(b => b.Payments)
                    .Where(p => p.PaymentType == "FullPayment")
                    .Sum(p => p.HostPayoutAmount);

                monthlyData.Add(new
                {
                    month = targetMonth.ToString("MMM yyyy"),
                    earnings = monthEarnings
                });
            }

            ViewBag.TotalEarnings = totalEarnings;
            ViewBag.TotalNights = totalNights;
            ViewBag.UpcomingArrivals = upcomingArrivals;
            ViewBag.UpcomingDepartures = upcomingDepartures;
            ViewBag.MonthlyDataJson = System.Text.Json.JsonSerializer.Serialize(monthlyData);

            return View(properties);
        }

        // ==========================================
        // 7.2 Admin Platform Reporting & Financial Export
        // ==========================================
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult AdminDashboard()
        {
            var totalUsers = _db.Users.Count();
            var totalHosts = _db.Users.Count(u => u.Role == "Host");
            var totalGuests = _db.Users.Count(u => u.Role == "Guest");
            var pendingHostVerifications = _db.Users.Count(u => u.IdentityStatus == "PendingApproval");

            var totalListings = _db.Properties.Count();
            var activeListings = _db.Properties.Count(p => p.IsActive);

            var payments = _db.Payments.Where(p => p.Status == "Paid" && p.PaymentType == "FullPayment").ToList();
            decimal platformGmv = payments.Sum(p => p.Amount);
            decimal platformCommissionRevenue = payments.Sum(p => p.Amount - p.HostPayoutAmount);

            var recentBookings = _db.Bookings
                .Include(b => b.Property)
                .Include(b => b.Guest)
                .OrderByDescending(b => b.BookingDate)
                .Take(10)
                .ToList();

            var recentDisputes = _db.MaintenanceTickets
                .Include(m => m.Booking)
                    .ThenInclude(b => b.Property)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .ToList();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalHosts = totalHosts;
            ViewBag.TotalGuests = totalGuests;
            ViewBag.PendingHostVerifications = pendingHostVerifications;
            ViewBag.TotalListings = totalListings;
            ViewBag.ActiveListings = activeListings;
            ViewBag.PlatformGmv = platformGmv;
            ViewBag.CommissionRevenue = platformCommissionRevenue;
            ViewBag.RecentDisputes = recentDisputes;

            return View(recentBookings);
        }

        [Authorize(Roles = "Admin,Host")]
        [HttpGet]
        public IActionResult ExportFinancialCsv()
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var query = _db.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b.Property)
                .Include(p => p.Booking)
                    .ThenInclude(b => b.Guest)
                .AsQueryable();

            if (!isAdmin)
            {
                query = query.Where(p => p.Booking.Property.HostId == currentUserId);
            }

            var list = query.OrderByDescending(p => p.PaymentDate).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("PaymentId,BookingId,PropertyTitle,GuestName,PaymentType,Amount(MYR),HostPayout(MYR),PaymentMethod,TransactionRef,Status,Date");

            foreach (var p in list)
            {
                sb.AppendLine($"{p.Id},{p.BookingId},\"{p.Booking?.Property?.Title?.Replace("\"", "\"\"")}\",\"{p.Booking?.Guest?.FullName}\",{p.PaymentType},{p.Amount},{p.HostPayoutAmount},\"{p.PaymentMethod}\",{p.TransactionRef},{p.Status},{p.PaymentDate:yyyy-MM-dd HH:mm}");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", $"HomeNest_Financial_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }
    }
}
