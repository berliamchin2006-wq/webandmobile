using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HomeNest.Models;

namespace HomeNest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            string appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            AppDomain.CurrentDomain.SetData("DataDirectory", appDataPath);

            builder.Services.AddControllersWithViews();

            builder.Services.AddDbContext<DB>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<Helper>();

            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Login";
                    options.LogoutPath = "/Account/Logout";
                    options.AccessDeniedPath = "/Account/AccessDenied";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                });

            builder.Services.AddSession();

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DB>();
                var helper = scope.ServiceProvider.GetRequiredService<Helper>();
                try
                {
                    db.Database.EnsureCreated();
                    SeedSampleData(db, helper);
                }
                catch (Exception ex)
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "An error occurred while creating or seeding the database.");
                }
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Property}/{action=Catalog}/{id?}");

            app.Run();
        }

        public static void SeedSampleData(DB db, Helper helper)
        {
            if (db.Users.Any()) return;

            var admin = new User
            {
                Email = "admin@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "System Administrator",
                PhoneNumber = "012-3456789",
                Role = "Admin",
                ProfilePhoto = "default.png",
                IsEmailVerified = true
            };

            var owner = new User
            {
                Email = "owner1@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "Tan Ah Kow",
                PhoneNumber = "016-8889999",
                Role = "Owner",
                ProfilePhoto = "default.png",
                IsEmailVerified = true
            };

            var tenant = new User
            {
                Email = "tenant1@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "Nurul Aina",
                PhoneNumber = "011-22334455",
                Role = "Tenant",
                ProfilePhoto = "default.png",
                IsEmailVerified = true
            };

            db.Users.AddRange(admin, owner, tenant);
            db.SaveChanges();

            // Property 1: PV15 Setapak
            var p1 = new Property
            {
                OwnerId = owner.Id,
                Title = "PV15 Platinum Lake Condominium (Walking Distance to TAR UMT)",
                PropertyType = "Condominium",
                Address = "Jalan Danau Saujana, Taman Danau Kota",
                City = "Setapak",
                State = "Kuala Lumpur",
                Postcode = "53300",
                Latitude = 3.2015,
                Longitude = 101.7168,
                Description = "Fully furnished student & working adult friendly condo with 100Mbps TIME Wi-Fi, water heater, washing machine, and refrigerator. 5-minute walk to TAR UMT bus stop and PV128 food street.",
                AmenitiesCsv = "WiFi,AirCond,NearLRT,CookingAllowed,WashingMachine,SwimmingPool,Gym",
                IsActive = true
            };

            // Property 2: Sunway Geo
            var p2 = new Property
            {
                OwnerId = owner.Id,
                Title = "Sunway Geo Residences (Direct Canopy Walk to Sunway & Monash)",
                PropertyType = "Condominium",
                Address = "Jalan Lagoon Selatan, Sunway South Quay",
                City = "Bandar Sunway",
                State = "Selangor",
                Postcode = "47500",
                Latitude = 3.0645,
                Longitude = 101.6035,
                Description = "High-end serviced residence with elevated canopy walkway connected directly to Sunway University and Monash University. Features 24-hr multi-tier security, Olympic-length pool, and retail conveniences below.",
                AmenitiesCsv = "WiFi,AirCond,NearLRT,CookingAllowed,Gym,SwimmingPool,Security24Hr",
                IsActive = true
            };

            // Property 3: University Green Semenyih
            var p3 = new Property
            {
                OwnerId = owner.Id,
                Title = "University Green Townhouse (Walking Distance to Nottingham)",
                PropertyType = "Landed House",
                Address = "Jalan Broga, Taman Universiti",
                City = "Semenyih",
                State = "Selangor",
                Postcode = "43500",
                Latitude = 2.9450,
                Longitude = 101.8732,
                Description = "Gated & guarded landed townhouse located 800m from University of Nottingham Malaysia campus. Quiet environment with spacious study spaces, high-speed mesh broadband, and free private parking.",
                AmenitiesCsv = "WiFi,AirCond,CookingAllowed,WashingMachine,Security24Hr",
                IsActive = true
            };

            // Property 4: Vista Komanwel Bukit Jalil
            var p4 = new Property
            {
                OwnerId = owner.Id,
                Title = "Vista Komanwel B (Opposite IMU & Sri Petaling / Bukit Jalil LRT)",
                PropertyType = "Condominium",
                Address = "Jalan Jalil Perkasa 19, Bukit Jalil",
                City = "Bukit Jalil",
                State = "Kuala Lumpur",
                Postcode = "57000",
                Latitude = 3.0580,
                Longitude = 101.6912,
                Description = "Opposite International Medical University (IMU) and 3 minutes walk to Sri Petaling LRT station. Fully furnished with study desk, ergonomic chair, quiet study atmosphere, and card access security.",
                AmenitiesCsv = "WiFi,AirCond,NearLRT,CookingAllowed,WashingMachine,SwimmingPool",
                IsActive = true
            };

            db.Properties.AddRange(p1, p2, p3, p4);
            db.SaveChanges();

            // Photos
            db.PropertyPhotos.AddRange(
                new PropertyPhoto { PropertyId = p1.Id, FileName = "https://images.unsplash.com/photo-1522708323590-d24dbb6b0267?w=800", IsCover = true },
                new PropertyPhoto { PropertyId = p2.Id, FileName = "https://images.unsplash.com/photo-1502672260266-1c1ef2d93688?w=800", IsCover = true },
                new PropertyPhoto { PropertyId = p3.Id, FileName = "https://images.unsplash.com/photo-1493809842364-78817add7ffb?w=800", IsCover = true },
                new PropertyPhoto { PropertyId = p4.Id, FileName = "https://images.unsplash.com/photo-1560448204-e02f11c3d0e2?w=800", IsCover = true }
            );

            // Room Units for P1
            var r1 = new RoomUnit { PropertyId = p1.Id, RoomName = "Master Bedroom with Private Bath", RoomType = "Master Room", MonthlyRent = 850.00m, SecurityDeposit = 1700.00m, UtilityDeposit = 425.00m, MaxPax = 2, IsAvailable = true };
            var r2 = new RoomUnit { PropertyId = p1.Id, RoomName = "Medium Balcony Room", RoomType = "Medium Room", MonthlyRent = 600.00m, SecurityDeposit = 1200.00m, UtilityDeposit = 300.00m, MaxPax = 1, IsAvailable = true };
            var r3 = new RoomUnit { PropertyId = p1.Id, RoomName = "Single Cozy Room", RoomType = "Single Room", MonthlyRent = 450.00m, SecurityDeposit = 900.00m, UtilityDeposit = 225.00m, MaxPax = 1, IsAvailable = true };

            // Room Units for P2
            var r4 = new RoomUnit { PropertyId = p2.Id, RoomName = "Executive Studio Suite", RoomType = "Studio", MonthlyRent = 1200.00m, SecurityDeposit = 2400.00m, UtilityDeposit = 600.00m, MaxPax = 2, IsAvailable = true };
            var r5 = new RoomUnit { PropertyId = p2.Id, RoomName = "Premium Single Room", RoomType = "Single Room", MonthlyRent = 800.00m, SecurityDeposit = 1600.00m, UtilityDeposit = 400.00m, MaxPax = 1, IsAvailable = true };

            // Room Units for P3
            var r6 = new RoomUnit { PropertyId = p3.Id, RoomName = "Master Suite with Garden View", RoomType = "Master Room", MonthlyRent = 750.00m, SecurityDeposit = 1500.00m, UtilityDeposit = 375.00m, MaxPax = 2, IsAvailable = true };
            var r7 = new RoomUnit { PropertyId = p3.Id, RoomName = "Single Study Room", RoomType = "Single Room", MonthlyRent = 500.00m, SecurityDeposit = 1000.00m, UtilityDeposit = 250.00m, MaxPax = 1, IsAvailable = true };

            // Room Units for P4
            var r8 = new RoomUnit { PropertyId = p4.Id, RoomName = "IMU Student Ensuite Room", RoomType = "Master Room", MonthlyRent = 900.00m, SecurityDeposit = 1800.00m, UtilityDeposit = 450.00m, MaxPax = 1, IsAvailable = true };
            var r9 = new RoomUnit { PropertyId = p4.Id, RoomName = "Medium LRT View Room", RoomType = "Medium Room", MonthlyRent = 650.00m, SecurityDeposit = 1300.00m, UtilityDeposit = 325.00m, MaxPax = 1, IsAvailable = true };

            db.RoomUnits.AddRange(r1, r2, r3, r4, r5, r6, r7, r8, r9);
            db.SaveChanges();

            // Seed 1 sample viewing appointment & booking for Nurul Aina to display pass badge
            var viewing = new ViewingAppointment
            {
                RoomUnitId = r1.Id,
                TenantId = tenant.Id,
                AppointmentDateTime = DateTime.Today.AddDays(2).AddHours(14),
                Status = "Confirmed",
                Remarks = "Scheduled physical viewing via pass",
                QrCodeToken = "PASS-" + Guid.NewGuid().ToString("N")[..8].ToUpper()
            };
            db.ViewingAppointments.Add(viewing);

            var booking = new TenancyBooking
            {
                RoomUnitId = r2.Id,
                TenantId = tenant.Id,
                StartDate = DateTime.Today.AddDays(14),
                EndDate = DateTime.Today.AddDays(14).AddMonths(12),
                DurationMonths = 12,
                AgreedMonthlyRent = r2.MonthlyRent,
                TotalDepositPaid = r2.SecurityDeposit + r2.UtilityDeposit + r2.MonthlyRent,
                Status = "PendingApproval",
                BookingDate = DateTime.Now
            };
            db.TenancyBookings.Add(booking);
            db.SaveChanges();
        }
    }
}