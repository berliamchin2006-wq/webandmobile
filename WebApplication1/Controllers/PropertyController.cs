using HomeNest.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

        [HttpGet]
        public IActionResult Catalog(
            string? keyword = null,
            string? propertyType = null,
            decimal? maxRent = null,
            string[]? amenities = null,
            string? sort = "featured",
            int page = 1)
        {
            int pageSize = 12;

            var query = _db.Properties
                .Include(p => p.Photos)
                .Include(p => p.RoomUnits)
                .Where(p => p.IsActive)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string kw = keyword.Trim();
                query = query.Where(p =>
                    p.Title.Contains(kw) ||
                    p.City.Contains(kw) ||
                    p.Address.Contains(kw) ||
                    p.Description.Contains(kw));
            }

            if (!string.IsNullOrWhiteSpace(propertyType) && propertyType != "All")
            {
                query = query.Where(p => p.PropertyType == propertyType);
            }

            if (maxRent.HasValue && maxRent.Value > 0)
            {
                query = query.Where(p => p.RoomUnits.Any(r => r.IsAvailable && r.MonthlyRent <= maxRent.Value));
            }

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

            query = sort switch
            {
                "price_asc" => query.OrderBy(p => p.RoomUnits.Min(r => (decimal?)r.MonthlyRent) ?? 0m),
                "price_desc" => query.OrderByDescending(p => p.RoomUnits.Max(r => (decimal?)r.MonthlyRent) ?? 0m),
                "newest" => query.OrderByDescending(p => p.CreatedAt),
                _ => query.OrderByDescending(p => p.Id) // Featured & Verified
            };

            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            page = Math.Clamp(page, 1, Math.Max(1, totalPages));

            var properties = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            int allCount = _db.Properties.Count(p => p.IsActive);

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.Keyword = keyword;
            ViewBag.PropertyType = propertyType;
            ViewBag.MaxRent = maxRent ?? 2500m;
            ViewBag.Sort = sort ?? "featured";
            ViewBag.SelectedAmenities = amenities ?? Array.Empty<string>();
            ViewBag.TotalProperties = allCount;
            ViewBag.MatchingCount = totalItems;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView("_PropertyCatalogGrid", properties);
            }

            return View(properties);
        }

        [HttpGet]
        public IActionResult Details(int id)
        {
            var prop = _db.Properties
                .Include(p => p.Owner)
                .Include(p => p.Photos)
                .Include(p => p.RoomUnits)
                .FirstOrDefault(p => p.Id == id);

            if (prop == null) return NotFound();
            return View(prop);
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpGet]
        public IActionResult MyProperties()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var list = _db.Properties
                .Include(p => p.RoomUnits)
                .Include(p => p.Photos)
                .Where(p => User.IsInRole("Admin") || p.OwnerId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(list);
        }

        [Authorize(Roles = "Owner")]
        [HttpGet]
        public IActionResult Create()
        {
            return View(new PropertyFormVM());
        }

        [Authorize(Roles = "Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(PropertyFormVM vm)
        {
            if (vm.Photos != null && vm.Photos.Count > 0)
            {
                foreach (var file in vm.Photos)
                {
                    var err = _helper.ValidatePhoto(file);
                    if (err != null)
                    {
                        ModelState.AddModelError(nameof(vm.Photos), err);
                        break;
                    }
                }
            }

            if (!ModelState.IsValid)
                return View(vm);

            int ownerId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var prop = new Property
            {
                OwnerId = ownerId,
                Title = vm.Title.Trim(),
                PropertyType = vm.PropertyType,
                Address = vm.Address.Trim(),
                City = vm.City.Trim(),
                State = vm.State,
                Postcode = vm.Postcode.Trim(),
                Latitude = vm.Latitude,
                Longitude = vm.Longitude,
                Description = vm.Description.Trim(),
                AmenitiesCsv = string.Join(",", vm.SelectedAmenities)
            };

            _db.Properties.Add(prop);
            _db.SaveChanges();

            if (vm.Photos != null && vm.Photos.Count > 0)
            {
                bool isFirst = true;
                foreach (var file in vm.Photos)
                {
                    string savedFileName = _helper.SavePhoto(file, "properties");
                    _db.PropertyPhotos.Add(new PropertyPhoto
                    {
                        PropertyId = prop.Id,
                        FileName = savedFileName,
                        IsCover = isFirst
                    });
                    isFirst = false;
                }
                _db.SaveChanges();
            }

            TempData["SuccessMessage"] = "Property created! Now add room units below.";
            return RedirectToAction("Details", new { id = prop.Id });
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var prop = _db.Properties.FirstOrDefault(p => p.Id == id && (User.IsInRole("Admin") || p.OwnerId == userId));
            if (prop == null) return NotFound();

            var vm = new PropertyFormVM
            {
                Id = prop.Id,
                Title = prop.Title,
                PropertyType = prop.PropertyType,
                Address = prop.Address,
                City = prop.City,
                State = prop.State,
                Postcode = prop.Postcode,
                Latitude = prop.Latitude,
                Longitude = prop.Longitude,
                Description = prop.Description,
                SelectedAmenities = (prop.AmenitiesCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            };

            return View(vm);
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(PropertyFormVM vm)
        {
            if (!ModelState.IsValid) return View(vm);

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var prop = _db.Properties.FirstOrDefault(p => p.Id == vm.Id && (User.IsInRole("Admin") || p.OwnerId == userId));
            if (prop == null) return NotFound();

            prop.Title = vm.Title.Trim();
            prop.PropertyType = vm.PropertyType;
            prop.Address = vm.Address.Trim();
            prop.City = vm.City.Trim();
            prop.State = vm.State;
            prop.Postcode = vm.Postcode.Trim();
            prop.Latitude = vm.Latitude;
            prop.Longitude = vm.Longitude;
            prop.Description = vm.Description.Trim();
            prop.AmenitiesCsv = string.Join(",", vm.SelectedAmenities);

            _db.SaveChanges();
            TempData["SuccessMessage"] = "Property details updated successfully.";
            return RedirectToAction("Details", new { id = prop.Id });
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var prop = _db.Properties
                .Include(p => p.RoomUnits)
                .FirstOrDefault(p => p.Id == id && (User.IsInRole("Admin") || p.OwnerId == userId));

            if (prop != null)
            {
                // Soft delete by deactivating property
                prop.IsActive = !prop.IsActive;
                _db.SaveChanges();
                TempData["SuccessMessage"] = prop.IsActive ? "Property activated." : "Property deactivated from public catalog.";
            }

            return RedirectToAction("MyProperties");
        }

        // ==========================================
        // MODULE 3: ROOM UNIT MANAGEMENT
        // ==========================================

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddRoom(RoomUnitFormVM vm)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var prop = _db.Properties.FirstOrDefault(p => p.Id == vm.PropertyId && (User.IsInRole("Admin") || p.OwnerId == userId));
            if (prop == null) return NotFound();

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid room data. Please check input values.";
                return RedirectToAction("Details", new { id = vm.PropertyId });
            }

            var room = new RoomUnit
            {
                PropertyId = vm.PropertyId,
                RoomName = vm.RoomName.Trim(),
                RoomType = vm.RoomType,
                MonthlyRent = vm.MonthlyRent,
                SecurityDeposit = vm.SecurityDeposit > 0 ? vm.SecurityDeposit : vm.MonthlyRent * 2,
                UtilityDeposit = vm.UtilityDeposit > 0 ? vm.UtilityDeposit : vm.MonthlyRent * 0.5m,
                MaxPax = vm.MaxPax,
                IsAvailable = true
            };

            _db.RoomUnits.Add(room);
            _db.SaveChanges();

            TempData["SuccessMessage"] = $"Room '{room.RoomName}' added successfully.";
            return RedirectToAction("Details", new { id = vm.PropertyId });
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleRoomAvailability(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var room = _db.RoomUnits.Include(r => r.Property).FirstOrDefault(r => r.Id == id && (User.IsInRole("Admin") || r.Property.OwnerId == userId));
            if (room != null)
            {
                room.IsAvailable = !room.IsAvailable;
                _db.SaveChanges();
                TempData["SuccessMessage"] = $"Room '{room.RoomName}' is now marked as {(room.IsAvailable ? "Available" : "Occupied")}.";
                return RedirectToAction("Details", new { id = room.PropertyId });
            }
            return RedirectToAction("MyProperties");
        }

        [Authorize(Roles = "Owner,Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteRoom(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var room = _db.RoomUnits.Include(r => r.Property).FirstOrDefault(r => r.Id == id && (User.IsInRole("Admin") || r.Property.OwnerId == userId));
            if (room != null)
            {
                int pId = room.PropertyId;
                _db.RoomUnits.Remove(room);
                _db.SaveChanges();
                TempData["SuccessMessage"] = "Room unit deleted.";
                return RedirectToAction("Details", new { id = pId });
            }
            return RedirectToAction("MyProperties");
        }
    }
}