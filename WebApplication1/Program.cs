using System.Security.Claims;
using HomeNest.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

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
            builder.Services.AddScoped<HomeNest.Services.EmailService>();

            // Manual Cookie-Based Authentication
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Login";
                    options.LogoutPath = "/Account/Logout";
                    options.AccessDeniedPath = "/Account/AccessDenied";
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                });

            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(2);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            var app = builder.Build();

            // ==========================================
            // Database Migration / Re-creation & Seeding
            // ==========================================
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DB>();
                var helper = scope.ServiceProvider.GetRequiredService<Helper>();

                try
                {
                    bool needRecreate = false;
                    try
                    {
                        // Check if new schema column exists
                        _ = db.Properties.Select(p => p.BaseNightlyRate).FirstOrDefault();
                    }
                    catch
                    {
                        needRecreate = true;
                    }

                    if (needRecreate)
                    {
                        db.Database.EnsureDeleted();
                        db.Database.EnsureCreated();
                        SeedSampleData(db, helper);
                    }
                    else
                    {
                        db.Database.EnsureCreated();
                        if (!db.Users.Any())
                        {
                            SeedSampleData(db, helper);
                        }
                    }
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
                app.UseHttpsRedirection();
            }
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

            // 1. Seed Users (Admin, Hosts, Guests)
            var admin = new User
            {
                Email = "admin@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "System Administrator",
                PhoneNumber = "012-3456789",
                Role = "Admin",
                ProfilePhoto = "default.png",
                IsEmailVerified = true,
                IdentityStatus = "Verified",
                ThemePreference = "light",
                PreferredCurrency = "MYR",
                CreatedAt = DateTime.Now.AddDays(-60)
            };

            var host1 = new User
            {
                Email = "host1@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "Tan Ah Kow (Superhost)",
                PhoneNumber = "016-8889999",
                Role = "Host",
                ProfilePhoto = "default.png",
                IsEmailVerified = true,
                IdentityStatus = "Verified",
                IdentityApprovedAt = DateTime.Now.AddDays(-30),
                BankName = "Maybank",
                BankAccountNumberEncrypted = helper.Encrypt("514012345678"),
                BankHolderName = "Tan Ah Kow",
                ThemePreference = "light",
                PreferredCurrency = "MYR",
                CreatedAt = DateTime.Now.AddDays(-45)
            };

            var host2 = new User
            {
                Email = "host2@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "Lim Mei Ling",
                PhoneNumber = "017-5551234",
                Role = "Host",
                ProfilePhoto = "default.png",
                IsEmailVerified = true,
                IdentityStatus = "PendingApproval",
                IdentityNotes = "Submitted Malaysian MyKad IC for approval.",
                BankName = "CIMB Bank",
                BankAccountNumberEncrypted = helper.Encrypt("701234567890"),
                BankHolderName = "Lim Mei Ling",
                ThemePreference = "light",
                PreferredCurrency = "MYR",
                CreatedAt = DateTime.Now.AddDays(-10)
            };

            var guest1 = new User
            {
                Email = "guest1@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "Nurul Aina",
                PhoneNumber = "011-22334455",
                Role = "Guest",
                ProfilePhoto = "default.png",
                IsEmailVerified = true,
                IdentityStatus = "Verified",
                ThemePreference = "light",
                PreferredCurrency = "MYR",
                CreatedAt = DateTime.Now.AddDays(-30)
            };

            var guest2 = new User
            {
                Email = "guest2@homenest.my",
                PasswordHash = helper.HashPassword("Password123!"),
                FullName = "Jason Wong",
                PhoneNumber = "019-3344556",
                Role = "Guest",
                ProfilePhoto = "default.png",
                IsEmailVerified = true,
                IdentityStatus = "Unverified",
                ThemePreference = "light",
                PreferredCurrency = "MYR",
                CreatedAt = DateTime.Now.AddDays(-15)
            };

            db.Users.AddRange(admin, host1, host2, guest1, guest2);
            db.SaveChanges();

            // 2. Seed Realistic Malaysian Airbnb Listings
            var p1 = new Property
            {
                HostId = host1.Id,
                Title = "KLCC Sky Residence with Infinity Twin Towers View",
                PropertyType = "Condominium",
                Address = "199 Jalan Tun Razak, City Centre",
                City = "Kuala Lumpur",
                State = "Kuala Lumpur",
                Postcode = "50400",
                Latitude = 3.1579,
                Longitude = 101.7116,
                Description = "Luxury 2-bedroom high-floor residence featuring panoramic unobstructed views of the iconic Petronas Twin Towers. Access to rooftop 51st-floor infinity pool, modern gym, and walking distance to KLCC Park & Suria KLCC mall. Perfect for staycations and business travelers.",
                MaxGuests = 4,
                Bedrooms = 2,
                Beds = 2,
                Bathrooms = 2,
                BaseNightlyRate = 280m,
                CleaningFee = 60m,
                CustomWeekendRate = 340m,
                CancellationPolicy = "Flexible",
                HouseRules = "Check-in: 3:00 PM. Check-out: 11:00 AM. No smoking inside unit. No large parties. Please observe quiet hours after 10:00 PM.",
                AmenitiesCsv = "WiFi,AirCond,Kitchen,FreeParking,TV,Pool,Gym,SelfCheckIn",
                IsActive = true,
                CreatedAt = DateTime.Now.AddDays(-40)
            };

            var p2 = new Property
            {
                HostId = host1.Id,
                Title = "Georgetown Heritage Loft & Shophouse Retreat",
                PropertyType = "Townhouse",
                Address = "88 Armenian Street, UNESCO World Heritage Zone",
                City = "George Town",
                State = "Penang",
                Postcode = "10200",
                Latitude = 5.4150,
                Longitude = 100.3364,
                Description = "Step back in time inside a restored 1920s Peranakan shophouse, seamlessly blended with modern bohemian comforts. Located in the heart of George Town's historic art district, steps away from famous street murals, artisanal cafes, and famous Penang street food.",
                MaxGuests = 5,
                Bedrooms = 2,
                Beds = 3,
                Bathrooms = 2,
                BaseNightlyRate = 220m,
                CleaningFee = 50m,
                CustomWeekendRate = 260m,
                CancellationPolicy = "Moderate",
                HouseRules = "Check-in: 3:00 PM. Heritage wooden floors, please remove shoes before entering. Strictly no indoor smoking or open flames.",
                AmenitiesCsv = "WiFi,AirCond,Kitchen,TV,SelfCheckIn",
                IsActive = true,
                CreatedAt = DateTime.Now.AddDays(-35)
            };

            var p3 = new Property
            {
                HostId = host2.Id,
                Title = "Cameron Highlands English Cottage with Tea Plantation Garden",
                PropertyType = "Chalet",
                Address = "Batu 33, Jalan Besar, Ringlet",
                City = "Cameron Highlands",
                State = "Pahang",
                Postcode = "39000",
                Latitude = 4.4721,
                Longitude = 101.3756,
                Description = "Charming Tudor-style mountain cottage nestled amidst cool misty hills and vibrant strawberry farms. Features a functional brick fireplace, private outdoor tea deck overlooking vegetable valleys, and fresh mountain air.",
                MaxGuests = 6,
                Bedrooms = 3,
                Beds = 4,
                Bathrooms = 2,
                BaseNightlyRate = 350m,
                CleaningFee = 75m,
                CustomWeekendRate = 420m,
                CancellationPolicy = "Strict",
                HouseRules = "Check-in: 2:00 PM. Check-out: 12:00 PM. Firewood provided upon request. Family-friendly, pets allowed in outdoor garden.",
                AmenitiesCsv = "WiFi,Kitchen,FreeParking,TV,SelfCheckIn",
                IsActive = true,
                CreatedAt = DateTime.Now.AddDays(-20)
            };

            var p4 = new Property
            {
                HostId = host1.Id,
                Title = "Sunway Resort Theme Park View Penthouse Suite",
                PropertyType = "Condominium",
                Address = "Jalan Lagoon Selatan, Bandar Sunway",
                City = "Subang Jaya",
                State = "Selangor",
                Postcode = "47500",
                Latitude = 3.0673,
                Longitude = 101.6038,
                Description = "Spacious family penthouse connected directly to Sunway Pyramid Mall and Sunway Lagoon Theme Park via covered canopy walkway. Includes arcade room, high-speed WiFi, Disney+ on 65-inch 4K TV, and Olympic-length swimming pool.",
                MaxGuests = 8,
                Bedrooms = 3,
                Beds = 5,
                Bathrooms = 3,
                BaseNightlyRate = 420m,
                CleaningFee = 80m,
                CustomWeekendRate = 490m,
                CancellationPolicy = "Flexible",
                HouseRules = "Check-in: 3:00 PM. No smoking. No excessive noise. Keycard deposit required upon arrival.",
                AmenitiesCsv = "WiFi,AirCond,Kitchen,FreeParking,TV,Pool,Gym,SelfCheckIn",
                IsActive = true,
                CreatedAt = DateTime.Now.AddDays(-15)
            };

            var p5 = new Property
            {
                HostId = host2.Id,
                Title = "Langkawi Cenang Sunset Beach Villa with Private Dip Pool",
                PropertyType = "Villa",
                Address = "Pantai Cenang Coastal Walk",
                City = "Langkawi",
                State = "Kedah",
                Postcode = "07000",
                Latitude = 6.2929,
                Longitude = 99.7289,
                Description = "Private seaside tropical villa directly located 50 meters from Pantai Cenang beach. Relax in your private plunge pool while watching the Andaman Sea sunset. Includes complimentary airport transfer and beach BBQ equipment.",
                MaxGuests = 4,
                Bedrooms = 2,
                Beds = 2,
                Bathrooms = 2,
                BaseNightlyRate = 580m,
                CleaningFee = 100m,
                CustomWeekendRate = 650m,
                CancellationPolicy = "Moderate",
                HouseRules = "Check-in: 3:00 PM. Beach towels provided. Strictly no outdoor noise past 11:00 PM.",
                AmenitiesCsv = "WiFi,AirCond,Kitchen,FreeParking,TV,Pool,SeaView,SelfCheckIn",
                IsActive = true,
                CreatedAt = DateTime.Now.AddDays(-12)
            };

            var p6 = new Property
            {
                HostId = host1.Id,
                Title = "Malacca Jonker Riverfront Studio with Heritage Balcony",
                PropertyType = "Studio",
                Address = "15 Lorong Hang Jebat, Waterfront",
                City = "Melaka",
                State = "Melaka",
                Postcode = "75200",
                Latitude = 2.1950,
                Longitude = 102.2486,
                Description = "Romantic studio right along the Malacca River, 2-minute stroll to famous Jonker Street Night Market. Enjoy a cup of Nanyang white coffee on your private balcony watching the river cruise boats drift past.",
                MaxGuests = 2,
                Bedrooms = 1,
                Beds = 1,
                Bathrooms = 1,
                BaseNightlyRate = 160m,
                CleaningFee = 40m,
                CustomWeekendRate = 195m,
                CancellationPolicy = "Flexible",
                HouseRules = "Check-in: 3:00 PM. Self check-in via electronic keypad. Please keep balcony door closed when AC is on.",
                AmenitiesCsv = "WiFi,AirCond,TV,SelfCheckIn",
                IsActive = true,
                CreatedAt = DateTime.Now.AddDays(-8)
            };

            db.Properties.AddRange(p1, p2, p3, p4, p5, p6);
            db.SaveChanges();

            // 3. Seed Property Photos (Unsplash High Quality Vacation/Home Photos)
            db.PropertyPhotos.AddRange(
                // KLCC
                new PropertyPhoto { PropertyId = p1.Id, FileName = "https://images.unsplash.com/photo-1502672260266-1c1ef2d93688?w=800&q=80", IsCover = true, DisplayOrder = 1 },
                new PropertyPhoto { PropertyId = p1.Id, FileName = "https://images.unsplash.com/photo-1560448204-e02f11c3d0e2?w=800&q=80", IsCover = false, DisplayOrder = 2 },
                new PropertyPhoto { PropertyId = p1.Id, FileName = "https://images.unsplash.com/photo-1584622650111-993a426fbf0a?w=800&q=80", IsCover = false, DisplayOrder = 3 },

                // Georgetown
                new PropertyPhoto { PropertyId = p2.Id, FileName = "https://images.unsplash.com/photo-1512917774080-9991f1c4c750?w=800&q=80", IsCover = true, DisplayOrder = 1 },
                new PropertyPhoto { PropertyId = p2.Id, FileName = "https://images.unsplash.com/photo-1513694203232-719a280e022f?w=800&q=80", IsCover = false, DisplayOrder = 2 },

                // Cameron Highlands
                new PropertyPhoto { PropertyId = p3.Id, FileName = "https://images.unsplash.com/photo-1518780664697-55e3ad937233?w=800&q=80", IsCover = true, DisplayOrder = 1 },
                new PropertyPhoto { PropertyId = p3.Id, FileName = "https://images.unsplash.com/photo-1542314831-068cd1dbfeeb?w=800&q=80", IsCover = false, DisplayOrder = 2 },

                // Sunway
                new PropertyPhoto { PropertyId = p4.Id, FileName = "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=800&q=80", IsCover = true, DisplayOrder = 1 },
                new PropertyPhoto { PropertyId = p4.Id, FileName = "https://images.unsplash.com/photo-1600607687939-ce8a6c25118c?w=800&q=80", IsCover = false, DisplayOrder = 2 },

                // Langkawi
                new PropertyPhoto { PropertyId = p5.Id, FileName = "https://images.unsplash.com/photo-1580587771525-78b9dba3b914?w=800&q=80", IsCover = true, DisplayOrder = 1 },
                new PropertyPhoto { PropertyId = p5.Id, FileName = "https://images.unsplash.com/photo-1512915922686-57c11dde9b6b?w=800&q=80", IsCover = false, DisplayOrder = 2 },

                // Melaka
                new PropertyPhoto { PropertyId = p6.Id, FileName = "https://images.unsplash.com/photo-1522708323590-d24dbb6b0267?w=800&q=80", IsCover = true, DisplayOrder = 1 },
                new PropertyPhoto { PropertyId = p6.Id, FileName = "https://images.unsplash.com/photo-1507089947368-19c1da9775ae?w=800&q=80", IsCover = false, DisplayOrder = 2 }
            );
            db.SaveChanges();

            // 4. Seed Sample Bookings & Payments
            var b1 = new Booking
            {
                PropertyId = p1.Id,
                GuestId = guest1.Id,
                CheckInDate = DateTime.Today.AddDays(-10),
                CheckOutDate = DateTime.Today.AddDays(-7),
                TotalNights = 3,
                GuestCount = 2,
                NightlyRate = 280m,
                SubtotalAmount = 840m,
                CleaningFee = 60m,
                ServiceFee = 84m,
                TotalAmount = 984m,
                Status = "CheckedOut",
                QrCheckInToken = "PASS-KLCC-001",
                CheckedInAt = DateTime.Today.AddDays(-10).AddHours(15),
                CheckedOutAt = DateTime.Today.AddDays(-7).AddHours(11),
                BookingDate = DateTime.Today.AddDays(-20)
            };

            var b2 = new Booking
            {
                PropertyId = p2.Id,
                GuestId = guest1.Id,
                CheckInDate = DateTime.Today.AddDays(5),
                CheckOutDate = DateTime.Today.AddDays(8),
                TotalNights = 3,
                GuestCount = 3,
                NightlyRate = 220m,
                SubtotalAmount = 660m,
                CleaningFee = 50m,
                ServiceFee = 66m,
                TotalAmount = 776m,
                Status = "Confirmed",
                QrCheckInToken = "PASS-PENANG-002",
                BookingDate = DateTime.Today.AddDays(-3)
            };

            var b3 = new Booking
            {
                PropertyId = p4.Id,
                GuestId = guest2.Id,
                CheckInDate = DateTime.Today.AddDays(-1),
                CheckOutDate = DateTime.Today.AddDays(2),
                TotalNights = 3,
                GuestCount = 4,
                NightlyRate = 420m,
                SubtotalAmount = 1260m,
                CleaningFee = 80m,
                ServiceFee = 126m,
                TotalAmount = 1466m,
                Status = "CheckedIn",
                QrCheckInToken = "PASS-SUNWAY-003",
                CheckedInAt = DateTime.Today.AddDays(-1).AddHours(16),
                BookingDate = DateTime.Today.AddDays(-7)
            };

            db.Bookings.AddRange(b1, b2, b3);
            db.SaveChanges();

            // Payments
            db.Payments.AddRange(
                new Payment
                {
                    BookingId = b1.Id,
                    PaymentType = "FullPayment",
                    Amount = 984m,
                    PaymentDate = b1.BookingDate,
                    PaymentMethod = "Stripe Credit Card (ending in 4242)",
                    TransactionRef = "TXN-KLCC-99881",
                    Status = "Paid",
                    HostPayoutAmount = 874.80m,
                    HostPayoutStatus = "Disbursed",
                    HostPayoutDisbursedAt = DateTime.Today.AddDays(-6)
                },
                new Payment
                {
                    BookingId = b2.Id,
                    PaymentType = "FullPayment",
                    Amount = 776m,
                    PaymentDate = b2.BookingDate,
                    PaymentMethod = "FPX Online Banking (Maybank2u)",
                    TransactionRef = "TXN-PENANG-77412",
                    Status = "Paid",
                    HostPayoutAmount = 690.20m,
                    HostPayoutStatus = "Pending"
                },
                new Payment
                {
                    BookingId = b3.Id,
                    PaymentType = "FullPayment",
                    Amount = 1466m,
                    PaymentDate = b3.BookingDate,
                    PaymentMethod = "Touch 'n Go eWallet",
                    TransactionRef = "TXN-SUNWAY-33215",
                    Status = "Paid",
                    HostPayoutAmount = 1302.20m,
                    HostPayoutStatus = "Pending"
                }
            );
            db.SaveChanges();

            // 5. Seed Reviews with Multi-Criteria Scores
            db.Reviews.AddRange(
                new Review
                {
                    BookingId = b1.Id,
                    PropertyId = p1.Id,
                    GuestId = guest1.Id,
                    CleanlinessRating = 5,
                    AccuracyRating = 5,
                    CommunicationRating = 5,
                    LocationRating = 5,
                    OverallRating = 5.0,
                    Comment = "Absolutely breathtaking view of KLCC! The rooftop pool was magical at sunset. Tan was an exceptional Superhost, providing clear self check-in directions and great food recommendations nearby.",
                    HostRatingForGuest = 5,
                    HostReply = "Thank you so much Nurul! You were a wonderful guest, left the unit spotless. Welcome back anytime!",
                    IsPublished = true,
                    HelpfulVotes = 12,
                    CreatedAt = DateTime.Today.AddDays(-6)
                },
                new Review
                {
                    BookingId = b1.Id,
                    PropertyId = p2.Id,
                    GuestId = guest2.Id,
                    CleanlinessRating = 5,
                    AccuracyRating = 5,
                    CommunicationRating = 4,
                    LocationRating = 5,
                    OverallRating = 4.8,
                    Comment = "Authentic heritage shophouse vibe right in Armenian Street. You step out directly into street murals and artisanal coffee shops. Very comfortable beds and great air conditioning.",
                    IsPublished = true,
                    HelpfulVotes = 7,
                    CreatedAt = DateTime.Today.AddDays(-15)
                }
            );

            // 6. Seed Wishlists
            db.Wishlists.AddRange(
                new Wishlist { GuestId = guest1.Id, PropertyId = p2.Id, FolderName = "Penang Trip 2026", AddedAt = DateTime.Now.AddDays(-5) },
                new Wishlist { GuestId = guest1.Id, PropertyId = p5.Id, FolderName = "Penang Trip 2026", AddedAt = DateTime.Now.AddDays(-4) },
                new Wishlist { GuestId = guest1.Id, PropertyId = p3.Id, FolderName = "Mountain Getaways", AddedAt = DateTime.Now.AddDays(-2) }
            );

            // 7. Seed Initial Audit Logs
            helper.LogAudit(db, admin.Id, admin.Email, "SystemInitialized", "System successfully seeded with Airbnb short-stay dataset.");
            helper.LogAudit(db, host1.Id, host1.Email, "HostVerified", "Host Tan Ah Kow verified as Superhost.");
        }
    }
}
