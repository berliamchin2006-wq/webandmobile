using System.Globalization;
using System.Security.Claims;
using HomeNest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HomeNest.Controllers
{
    public class PropertyController : Controller
    {
        private readonly DB _db;
        private readonly Helper _helper;

        public PropertyController(DB db, Helper helper)
        {
            _db = db;
            _helper = helper;
        }

        // ==========================================
        // 3.1 & 3.2 Property Catalog & AJAX Search
        // ==========================================
        [HttpGet]
        public IActionResult Catalog(
            string? keyword = null,
            string? propertyType = null,
            decimal? minPrice = null,
            decimal? maxPrice = null,
            DateTime? checkIn = null,
            DateTime? checkOut = null,
            int? guests = null,
            string[]? amenities = null,
            string? sort = "featured",
            int page = 1)
        {
            int pageSize = 9;

            var query = _db.Properties
                .Include(p => p.Host)
                .Include(p => p.Photos)
                .Include(p => p.Reviews)
                .Where(p => p.IsActive)
                .AsQueryable();

            // Keyword Filter (Title, City, State, Address, Description)
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string kw = keyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Title.ToLower().Contains(kw) ||
                    p.City.ToLower().Contains(kw) ||
                    p.State.ToLower().Contains(kw) ||
                    p.Address.ToLower().Contains(kw) ||
                    p.Description.ToLower().Contains(kw));
            }

            // Property Type Filter
            if (!string.IsNullOrWhiteSpace(propertyType) && propertyType != "All")
            {
                query = query.Where(p => p.PropertyType == propertyType);
            }

            // Guest Count Capacity Filter
            if (guests.HasValue && guests.Value > 1)
            {
                query = query.Where(p => p.MaxGuests >= guests.Value);
            }

            // Price Range Filter
            if (minPrice.HasValue && minPrice.Value > 0)
            {
                query = query.Where(p => p.BaseNightlyRate >= minPrice.Value);
            }
            if (maxPrice.HasValue && maxPrice.Value > 0)
            {
                query = query.Where(p => p.BaseNightlyRate <= maxPrice.Value);
            }

            // Real-Time Calendar Availability Filter (Excludes booked dates)
            if (checkIn.HasValue && checkOut.HasValue && checkOut.Value > checkIn.Value)
            {
                var cIn = checkIn.Value.Date;
                var cOut = checkOut.Value.Date;

                query = query.Where(p => !_db.Bookings.Any(b =>
                    b.PropertyId == p.Id &&
                    b.Status != "Cancelled" &&
                    cIn < b.CheckOutDate.Date &&
                    cOut > b.CheckInDate.Date));
            }

            // Amenities Filter (AND logic)
            if (amenities != null && amenities.Length > 0)
            {
                foreach (var am in amenities)
                {
                    if (!string.IsNullOrWhiteSpace(am))
                    {
                        query = query.Where(p => p.AmenitiesCsv.Contains(am));
                    }
                }
            }

            // Sorting
            query = sort switch
            {
                "price_asc" => query.OrderBy(p => p.BaseNightlyRate),
                "price_desc" => query.OrderByDescending(p => p.BaseNightlyRate),
                "rating" => query.OrderByDescending(p => p.Reviews.Any() ? p.Reviews.Average(r => r.OverallRating) : 0),
                "newest" => query.OrderByDescending(p => p.CreatedAt),
                _ => query.OrderByDescending(p => p.Id) // Featured & Verified first
            };

            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = Math.Clamp(page, 1, Math.Max(1, totalPages));

            var properties = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Populate User's Wishlist IDs if logged in
            HashSet<int> userWishlistPropertyIds = [];
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int currentUserId))
                {
                    userWishlistPropertyIds = _db.Wishlists
                        .Where(w => w.GuestId == currentUserId)
                        .Select(w => w.PropertyId)
                        .ToHashSet();
                }
            }

            ViewBag.WishlistPropertyIds = userWishlistPropertyIds;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.Keyword = keyword;
            ViewBag.PropertyType = propertyType;
            ViewBag.MinPrice = minPrice ?? 50m;
            ViewBag.MaxPrice = maxPrice ?? 1500m;
            ViewBag.CheckIn = checkIn?.ToString("yyyy-MM-dd");
            ViewBag.CheckOut = checkOut?.ToString("yyyy-MM-dd");
            ViewBag.Guests = guests ?? 1;
            ViewBag.Sort = sort ?? "featured";
            ViewBag.SelectedAmenities = amenities ?? Array.Empty<string>();
            ViewBag.TotalProperties = _db.Properties.Count(p => p.IsActive);
            ViewBag.MatchingCount = totalItems;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_PropertyCatalogGrid", properties);
            }

            return View(properties);
        }

        // ==========================================
        // 3.1 & 3.2 Property Details Page
        // ==========================================
        [HttpGet]
        public IActionResult Details(int id, DateTime? checkIn = null, DateTime? checkOut = null, int guests = 1)
        {
            var property = _db.Properties
                .Include(p => p.Host)
                .Include(p => p.Photos.OrderBy(ph => ph.DisplayOrder))
                .Include(p => p.Reviews)
                    .ThenInclude(r => r.Guest)
                .FirstOrDefault(p => p.Id == id);

            if (property == null) return NotFound();

            // Existing Booked Date Ranges for calendar blocking
            var bookedRanges = _db.Bookings
                .Where(b => b.PropertyId == id && b.Status != "Cancelled" && b.CheckOutDate >= DateTime.Today)
                .Select(b => new
                {
                    from = b.CheckInDate.ToString("yyyy-MM-dd"),
                    to = b.CheckOutDate.ToString("yyyy-MM-dd")
                })
                .ToList();

            ViewBag.BookedDateRangesJson = System.Text.Json.JsonSerializer.Serialize(bookedRanges);

            // Default dates if not passed
            DateTime cIn = checkIn ?? DateTime.Today.AddDays(1);
            DateTime cOut = checkOut ?? DateTime.Today.AddDays(3);
            if (cOut <= cIn) cOut = cIn.AddDays(1);

            int nights = Math.Max(1, (cOut.Date - cIn.Date).Days);
            decimal subtotal = nights * property.BaseNightlyRate;
            decimal cleaningFee = property.CleaningFee;
            decimal serviceFee = Math.Round(subtotal * 0.10m, 2);
            decimal total = subtotal + cleaningFee + serviceFee;

            ViewBag.CheckIn = cIn.ToString("yyyy-MM-dd");
            ViewBag.CheckOut = cOut.ToString("yyyy-MM-dd");
            ViewBag.Guests = Math.Clamp(guests, 1, property.MaxGuests);
            ViewBag.Nights = nights;
            ViewBag.Subtotal = subtotal;
            ViewBag.CleaningFee = cleaningFee;
            ViewBag.ServiceFee = serviceFee;
            ViewBag.Total = total;

            // Host Aggregated Rating
            var hostRatings = _db.Reviews.Where(r => r.Property.HostId == property.HostId).Select(r => r.OverallRating).ToList();
            ViewBag.HostAverageRating = hostRatings.Any() ? Math.Round(hostRatings.Average(), 2) : 5.0;
            ViewBag.HostTotalReviews = hostRatings.Count;
            ViewBag.IsSuperhost = hostRatings.Count >= 3 && ViewBag.HostAverageRating >= 4.8;

            // Check if saved to wishlist
            bool isSaved = false;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int currentUserId))
                {
                    isSaved = _db.Wishlists.Any(w => w.GuestId == currentUserId && w.PropertyId == id);
                }
            }
            ViewBag.IsSavedToWishlist = isSaved;

            return View(property);
        }

        // ==========================================
        // 3.2 Real-Time Availability & Price Calculator API
        // ==========================================
        [HttpGet]
        public IActionResult CheckAvailability(int propertyId, DateTime checkIn, DateTime checkOut, int guests = 1)
        {
            var property = _db.Properties.Find(propertyId);
            if (property == null) return Json(new { success = false, message = "Property not found." });

            if (checkOut <= checkIn)
            {
                return Json(new { success = false, message = "Check-out date must be after check-in date." });
            }

            // Check if dates conflict with active bookings
            bool isBooked = _db.Bookings.Any(b =>
                b.PropertyId == propertyId &&
                b.Status != "Cancelled" &&
                checkIn.Date < b.CheckOutDate.Date &&
                checkOut.Date > b.CheckInDate.Date);

            if (isBooked)
            {
                return Json(new { success = true, available = false, message = "Selected dates are unavailable." });
            }

            int nights = (checkOut.Date - checkIn.Date).Days;
            decimal subtotal = 0m;

            // Calculate daily rates (with weekend pricing)
            for (DateTime d = checkIn.Date; d < checkOut.Date; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Friday || d.DayOfWeek == DayOfWeek.Saturday)
                {
                    subtotal += property.CustomWeekendRate;
                }
                else
                {
                    subtotal += property.BaseNightlyRate;
                }
            }

            decimal cleaningFee = property.CleaningFee;
            decimal serviceFee = Math.Round(subtotal * 0.10m, 2);
            decimal total = subtotal + cleaningFee + serviceFee;

            return Json(new
            {
                success = true,
                available = true,
                nights,
                subtotal,
                cleaningFee,
                serviceFee,
                total
            });
        }

        // ==========================================
        // 2.1 & 2.2 Property Listing Management (CRUD)
        // ==========================================
        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult Create()
        {
            return View(new PropertyFormVM());
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(PropertyFormVM vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var property = new Property
            {
                HostId = currentUserId,
                Title = vm.Title.Trim(),
                PropertyType = vm.PropertyType,
                Address = vm.Address.Trim(),
                City = vm.City.Trim(),
                State = vm.State.Trim(),
                Postcode = vm.Postcode.Trim(),
                Latitude = vm.Latitude,
                Longitude = vm.Longitude,
                Description = vm.Description.Trim(),
                MaxGuests = vm.MaxGuests,
                Bedrooms = vm.Bedrooms,
                Beds = vm.Beds,
                Bathrooms = vm.Bathrooms,
                BaseNightlyRate = vm.BaseNightlyRate,
                CleaningFee = vm.CleaningFee,
                CustomWeekendRate = vm.CustomWeekendRate,
                CancellationPolicy = vm.CancellationPolicy,
                HouseRules = vm.HouseRules?.Trim() ?? "",
                AmenitiesCsv = vm.SelectedAmenities != null ? string.Join(",", vm.SelectedAmenities) : "",
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _db.Properties.Add(property);
            _db.SaveChanges();

            // Handle Multi-Photo Upload
            if (vm.NewPhotos != null && vm.NewPhotos.Count > 0)
            {
                bool isFirst = true;
                int order = 1;
                foreach (var photo in vm.NewPhotos)
                {
                    if (photo.Length > 0)
                    {
                        string fileName = _helper.SavePhoto(photo, "properties");
                        property.Photos.Add(new PropertyPhoto
                        {
                            PropertyId = property.Id,
                            FileName = fileName,
                            IsCover = isFirst,
                            DisplayOrder = order++
                        });
                        isFirst = false;
                    }
                }
                _db.SaveChanges();
            }

            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "ListingCreated",
                $"Host created property listing #{property.Id} '{property.Title}' in {property.City}.");

            TempData["SuccessMessage"] = $"Listing '{property.Title}' published successfully!";
            return RedirectToAction("MyProperties");
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var property = _db.Properties
                .Include(p => p.Photos.OrderBy(ph => ph.DisplayOrder))
                .FirstOrDefault(p => p.Id == id);

            if (property == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            var vm = new PropertyFormVM
            {
                Id = property.Id,
                Title = property.Title,
                PropertyType = property.PropertyType,
                Address = property.Address,
                City = property.City,
                State = property.State,
                Postcode = property.Postcode,
                Latitude = property.Latitude,
                Longitude = property.Longitude,
                Description = property.Description,
                MaxGuests = property.MaxGuests,
                Bedrooms = property.Bedrooms,
                Beds = property.Beds,
                Bathrooms = property.Bathrooms,
                BaseNightlyRate = property.BaseNightlyRate,
                CleaningFee = property.CleaningFee,
                CustomWeekendRate = property.CustomWeekendRate,
                CancellationPolicy = property.CancellationPolicy,
                HouseRules = property.HouseRules,
                SelectedAmenities = property.AmenitiesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                ExistingPhotos = property.Photos.ToList()
            };

            return View(vm);
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(PropertyFormVM vm)
        {
            var property = _db.Properties
                .Include(p => p.Photos)
                .FirstOrDefault(p => p.Id == vm.Id);

            if (property == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                vm.ExistingPhotos = property.Photos.OrderBy(ph => ph.DisplayOrder).ToList();
                return View(vm);
            }

            property.Title = vm.Title.Trim();
            property.PropertyType = vm.PropertyType;
            property.Address = vm.Address.Trim();
            property.City = vm.City.Trim();
            property.State = vm.State.Trim();
            property.Postcode = vm.Postcode.Trim();
            property.Latitude = vm.Latitude;
            property.Longitude = vm.Longitude;
            property.Description = vm.Description.Trim();
            property.MaxGuests = vm.MaxGuests;
            property.Bedrooms = vm.Bedrooms;
            property.Beds = vm.Beds;
            property.Bathrooms = vm.Bathrooms;
            property.BaseNightlyRate = vm.BaseNightlyRate;
            property.CleaningFee = vm.CleaningFee;
            property.CustomWeekendRate = vm.CustomWeekendRate;
            property.CancellationPolicy = vm.CancellationPolicy;
            property.HouseRules = vm.HouseRules?.Trim() ?? "";
            property.AmenitiesCsv = vm.SelectedAmenities != null ? string.Join(",", vm.SelectedAmenities) : "";

            // New Photos Upload
            if (vm.NewPhotos != null && vm.NewPhotos.Count > 0)
            {
                int maxOrder = property.Photos.Any() ? property.Photos.Max(ph => ph.DisplayOrder) : 0;
                foreach (var photo in vm.NewPhotos)
                {
                    if (photo.Length > 0)
                    {
                        string fileName = _helper.SavePhoto(photo, "properties");
                        property.Photos.Add(new PropertyPhoto
                        {
                            PropertyId = property.Id,
                            FileName = fileName,
                            IsCover = !property.Photos.Any(ph => ph.IsCover),
                            DisplayOrder = ++maxOrder
                        });
                    }
                }
            }

            // Cover Photo update
            if (vm.SelectedCoverPhotoId.HasValue)
            {
                foreach (var ph in property.Photos)
                {
                    ph.IsCover = ph.Id == vm.SelectedCoverPhotoId.Value;
                }
            }

            _db.SaveChanges();
            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "ListingUpdated",
                $"Host updated property listing #{property.Id} '{property.Title}'.");

            TempData["SuccessMessage"] = "Listing updated successfully!";
            return RedirectToAction("MyProperties");
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeletePhoto(int photoId)
        {
            var photo = _db.PropertyPhotos.Include(p => p.Property).FirstOrDefault(ph => ph.Id == photoId);
            if (photo == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (photo.Property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            int propId = photo.PropertyId;
            bool wasCover = photo.IsCover;
            _db.PropertyPhotos.Remove(photo);
            _db.SaveChanges();

            if (wasCover)
            {
                var nextPhoto = _db.PropertyPhotos.FirstOrDefault(p => p.PropertyId == propId);
                if (nextPhoto != null)
                {
                    nextPhoto.IsCover = true;
                    _db.SaveChanges();
                }
            }

            TempData["SuccessMessage"] = "Photo removed.";
            return RedirectToAction("Edit", new { id = propId });
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult MyProperties()
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            var properties = _db.Properties
                .Include(p => p.Photos)
                .Include(p => p.Bookings)
                .Include(p => p.Reviews)
                .Where(p => isAdmin || p.HostId == currentUserId)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(properties);
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleActive(int id)
        {
            var property = _db.Properties.Find(id);
            if (property == null) return NotFound();

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (property.HostId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            property.IsActive = !property.IsActive;
            _db.SaveChanges();

            TempData["SuccessMessage"] = $"Listing status changed to {(property.IsActive ? "Active / Published" : "Unlisted / Paused")}.";
            return RedirectToAction("MyProperties");
        }

        // ==========================================
        // 2.1 Batch Import / Update of Pricing via CSV
        // ==========================================
        [Authorize(Roles = "Host,Admin")]
        [HttpGet]
        public IActionResult BatchImport()
        {
            return View();
        }

        [Authorize(Roles = "Host,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BatchImport(BatchPricingImportVM vm)
        {
            if (vm.CsvFile == null || vm.CsvFile.Length == 0)
            {
                ModelState.AddModelError("", "Please select a valid CSV file.");
                return View();
            }

            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            bool isAdmin = User.IsInRole("Admin");

            int updatedCount = 0;
            var errorList = new List<string>();

            using (var reader = new StreamReader(vm.CsvFile.OpenReadStream()))
            {
                string? header = reader.ReadLine(); // PropertyId,NightlyRate,CleaningFee,WeekendRate,IsActive
                int lineNum = 1;

                while (!reader.EndOfStream)
                {
                    lineNum++;
                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(',');
                    if (parts.Length < 4)
                    {
                        errorList.Add($"Line {lineNum}: Insufficient columns. Format: PropertyId,NightlyRate,CleaningFee,WeekendRate,IsActive");
                        continue;
                    }

                    if (!int.TryParse(parts[0].Trim(), out int propId))
                    {
                        errorList.Add($"Line {lineNum}: Invalid PropertyId '{parts[0]}'");
                        continue;
                    }

                    var prop = _db.Properties.Find(propId);
                    if (prop == null || (!isAdmin && prop.HostId != currentUserId))
                    {
                        errorList.Add($"Line {lineNum}: Property #{propId} not found or you are not the host.");
                        continue;
                    }

                    if (decimal.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal nightly))
                    {
                        prop.BaseNightlyRate = nightly;
                    }

                    if (decimal.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cleaning))
                    {
                        prop.CleaningFee = cleaning;
                    }

                    if (decimal.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal weekend))
                    {
                        prop.CustomWeekendRate = weekend;
                    }

                    if (parts.Length >= 5 && bool.TryParse(parts[4].Trim(), out bool isActive))
                    {
                        prop.IsActive = isActive;
                    }

                    updatedCount++;
                }
            }

            _db.SaveChanges();
            _helper.LogAudit(_db, currentUserId, User.Identity?.Name ?? "", "BatchPricingImported",
                $"Host updated {updatedCount} property listings via batch CSV import.");

            ViewBag.UpdatedCount = updatedCount;
            ViewBag.ErrorList = errorList;
            TempData["SuccessMessage"] = $"Batch import finished! {updatedCount} listing(s) updated successfully.";
            return View();
        }

        // ==========================================
        // 3.1 Guest Wishlist & Trip Folders
        // ==========================================
        [Authorize]
        [HttpPost]
        public IActionResult ToggleWishlist(int propertyId, string? folderName)
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = _db.Wishlists.FirstOrDefault(w => w.GuestId == currentUserId && w.PropertyId == propertyId);

            bool isSaved;
            if (existing != null)
            {
                _db.Wishlists.Remove(existing);
                _db.SaveChanges();
                isSaved = false;
            }
            else
            {
                var wishlist = new Wishlist
                {
                    GuestId = currentUserId,
                    PropertyId = propertyId,
                    FolderName = string.IsNullOrWhiteSpace(folderName) ? "My Favorites" : folderName.Trim(),
                    AddedAt = DateTime.Now
                };
                _db.Wishlists.Add(wishlist);
                _db.SaveChanges();
                isSaved = true;
            }

            return Json(new { success = true, isSaved });
        }

        [Authorize]
        [HttpGet]
        public IActionResult Wishlist()
        {
            int currentUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var list = _db.Wishlists
                .Include(w => w.Property)
                    .ThenInclude(p => p.Photos)
                .Include(w => w.Property)
                    .ThenInclude(p => p.Reviews)
                .Where(w => w.GuestId == currentUserId)
                .OrderByDescending(w => w.AddedAt)
                .ToList();

            var folders = list.GroupBy(w => w.FolderName).ToDictionary(g => g.Key, g => g.ToList());
            return View(folders);
        }
    }
}
