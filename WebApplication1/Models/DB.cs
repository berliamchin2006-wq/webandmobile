using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HomeNest.Models
{
    public class DB : DbContext
    {
        public DB(DbContextOptions<DB> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Property> Properties { get; set; }
        public DbSet<PropertyPhoto> PropertyPhotos { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Wishlist> Wishlists { get; set; }
        public DbSet<MaintenanceTicket> MaintenanceTickets { get; set; }
        public DbSet<BookingMessage> BookingMessages { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
    }

    // =========================================================================
    // 1. USER & IDENTITY MANAGEMENT MODULE
    // =========================================================================
    [Index(nameof(Email), IsUnique = true)]
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100), EmailAddress]
        public string Email { get; set; } = "";

        [Required, MaxLength(255)]
        public string PasswordHash { get; set; } = "";

        [Required, MaxLength(100)]
        public string FullName { get; set; } = "";

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = "";

        // Roles: "Guest", "Host", "Admin"
        [Required, MaxLength(20)]
        public string Role { get; set; } = "Guest";

        [MaxLength(255)]
        public string ProfilePhoto { get; set; } = "default.png";

        // Email Verification
        public bool IsEmailVerified { get; set; } = false;

        [MaxLength(100)]
        public string? EmailVerificationToken { get; set; }

        // Temporary 3-Strike Lockout
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }

        // Password Reset
        [MaxLength(100)]
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }

        // Host Identity Verification (IC / Passport / License)
        [MaxLength(255)]
        public string? IdDocumentPhoto { get; set; }

        // Verification Status: "Unverified", "PendingApproval", "Verified", "Rejected"
        [MaxLength(30)]
        public string IdentityStatus { get; set; } = "Unverified";

        [MaxLength(500)]
        public string? IdentityNotes { get; set; }
        public DateTime? IdentityApprovedAt { get; set; }

        // Host Payout / Sensitive Bank Account Details (Encrypted/Masked)
        [MaxLength(100)]
        public string? BankName { get; set; }

        [MaxLength(255)]
        public string? BankAccountNumberEncrypted { get; set; }

        [MaxLength(100)]
        public string? BankHolderName { get; set; }

        // User UI Preferences
        [MaxLength(10)]
        public string ThemePreference { get; set; } = "light"; // "light" or "dark"

        [MaxLength(10)]
        public string PreferredCurrency { get; set; } = "MYR"; // "MYR", "USD", "SGD"

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        [InverseProperty(nameof(Property.Host))]
        public List<Property> HostedProperties { get; set; } = [];

        [InverseProperty(nameof(Booking.Guest))]
        public List<Booking> Bookings { get; set; } = [];

        [InverseProperty(nameof(Wishlist.Guest))]
        public List<Wishlist> Wishlists { get; set; } = [];
    }

    // =========================================================================
    // 2. PROPERTY & LISTING MANAGEMENT MODULE
    // =========================================================================
    public class Property
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int HostId { get; set; }

        [ForeignKey(nameof(HostId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Host { get; set; } = null!;

        [Required, MaxLength(150)]
        public string Title { get; set; } = "";

        // "Condominium", "Villa", "Bungalow", "Apartment", "Townhouse", "Studio", "Chalet"
        [Required, MaxLength(50)]
        public string PropertyType { get; set; } = "Condominium";

        [Required, MaxLength(255)]
        public string Address { get; set; } = "";

        [Required, MaxLength(50)]
        public string City { get; set; } = "";

        [Required, MaxLength(50)]
        public string State { get; set; } = "";

        [Required, MaxLength(10)]
        public string Postcode { get; set; } = "";

        // Interactive Map Coordinates
        public double Latitude { get; set; } = 3.1390;
        public double Longitude { get; set; } = 101.6869;

        [Required, MaxLength(2500)]
        public string Description { get; set; } = "";

        // Capacity & Space
        public int MaxGuests { get; set; } = 2;
        public int Bedrooms { get; set; } = 1;
        public int Beds { get; set; } = 1;
        public int Bathrooms { get; set; } = 1;

        // Pricing Configuration
        [Precision(10, 2)]
        public decimal BaseNightlyRate { get; set; } = 150m;

        [Precision(10, 2)]
        public decimal CleaningFee { get; set; } = 50m;

        [Precision(10, 2)]
        public decimal CustomWeekendRate { get; set; } = 180m;

        // Cancellation Policy: "Flexible", "Moderate", "Strict"
        [Required, MaxLength(30)]
        public string CancellationPolicy { get; set; } = "Flexible";

        // House Rules & Amenities
        [MaxLength(1000)]
        public string HouseRules { get; set; } = "Check-in after 3:00 PM. Check-out by 11:00 AM. No smoking. No parties or events. Quiet hours 10:00 PM - 8:00 AM.";

        [MaxLength(500)]
        public string AmenitiesCsv { get; set; } = "WiFi,AirCond,Kitchen,FreeParking,TV";

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public List<PropertyPhoto> Photos { get; set; } = [];
        public List<Booking> Bookings { get; set; } = [];
        public List<Review> Reviews { get; set; } = [];
        public List<Wishlist> Wishlists { get; set; } = [];
    }

    public class PropertyPhoto
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PropertyId { get; set; }

        [ForeignKey(nameof(PropertyId))]
        public Property Property { get; set; } = null!;

        [Required, MaxLength(255)]
        public string FileName { get; set; } = "";

        public bool IsCover { get; set; } = false;

        public int DisplayOrder { get; set; } = 0;
    }

    // =========================================================================
    // 3. PROPERTY DISCOVERY, SEARCH & BOOKING MODULE
    // =========================================================================
    public class Booking
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PropertyId { get; set; }

        [ForeignKey(nameof(PropertyId))]
        public Property Property { get; set; } = null!;

        [Required]
        public int GuestId { get; set; }

        [ForeignKey(nameof(GuestId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Guest { get; set; } = null!;

        [Required, DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        public int TotalNights { get; set; }
        public int GuestCount { get; set; } = 1;

        // Price Breakdown
        [Precision(10, 2)]
        public decimal NightlyRate { get; set; }

        [Precision(10, 2)]
        public decimal SubtotalAmount { get; set; }

        [Precision(10, 2)]
        public decimal CleaningFee { get; set; }

        [Precision(10, 2)]
        public decimal ServiceFee { get; set; } // 10% platform service fee

        [Precision(10, 2)]
        public decimal TotalAmount { get; set; }

        // State Machine: "Pending", "Confirmed", "CheckedIn", "CheckedOut", "Cancelled"
        [Required, MaxLength(30)]
        public string Status { get; set; } = "Confirmed";

        [MaxLength(500)]
        public string? SpecialRequests { get; set; }

        // Digital Check-in QR Token
        [Required, MaxLength(100)]
        public string QrCheckInToken { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime? CheckedInAt { get; set; }
        public DateTime? CheckedOutAt { get; set; }

        public DateTime BookingDate { get; set; } = DateTime.Now;

        // Navigation Properties
        public List<Payment> Payments { get; set; } = [];
        public List<MaintenanceTicket> MaintenanceTickets { get; set; } = [];
        public List<BookingMessage> Messages { get; set; } = [];
        public Review? Review { get; set; }
    }

    public class Wishlist
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int GuestId { get; set; }

        [ForeignKey(nameof(GuestId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Guest { get; set; } = null!;

        [Required]
        public int PropertyId { get; set; }

        [ForeignKey(nameof(PropertyId))]
        public Property Property { get; set; } = null!;

        [Required, MaxLength(50)]
        public string FolderName { get; set; } = "My Favorites";

        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    // =========================================================================
    // 4. PAYMENT & TRANSACTION MODULE
    // =========================================================================
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public Booking Booking { get; set; } = null!;

        [Required, MaxLength(50)]
        public string PaymentType { get; set; } = "FullPayment"; // "FullPayment", "Refund"

        [Precision(10, 2)]
        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        // "Stripe (Mock Card)", "FPX Online Banking", "Touch 'n Go eWallet"
        [Required, MaxLength(50)]
        public string PaymentMethod { get; set; } = "Stripe (Mock Card)";

        [Required, MaxLength(100)]
        public string TransactionRef { get; set; } = "";

        // Status: "Paid", "Refunded", "Failed"
        [Required, MaxLength(30)]
        public string Status { get; set; } = "Paid";

        // Host Earnings Ledger Tracking (Net after 3% host platform fee)
        [Precision(10, 2)]
        public decimal HostPayoutAmount { get; set; } = 0m;

        // "Pending", "Disbursed"
        [Required, MaxLength(30)]
        public string HostPayoutStatus { get; set; } = "Pending";

        public DateTime? HostPayoutDisbursedAt { get; set; }

        // Automated Refund Tracking
        [Precision(10, 2)]
        public decimal RefundAmount { get; set; } = 0m;

        [MaxLength(255)]
        public string? RefundReason { get; set; }
    }

    // =========================================================================
    // 5. IN-STAY & OPERATIONS MODULE
    // =========================================================================
    public class MaintenanceTicket
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public Booking Booking { get; set; } = null!;

        // "Air-Conditioning", "Plumbing", "Electrical / WiFi", "Cleanliness", "Appliances", "Other"
        [Required, MaxLength(50)]
        public string Category { get; set; } = "Other";

        [Required, MaxLength(150)]
        public string IssueTitle { get; set; } = "";

        [Required, MaxLength(1500)]
        public string Description { get; set; } = "";

        // "Low", "Medium", "High", "Urgent"
        [Required, MaxLength(20)]
        public string Priority { get; set; } = "Medium";

        [MaxLength(255)]
        public string? PhotoProof { get; set; }

        // "Reported", "In-Progress", "Resolved"
        [Required, MaxLength(30)]
        public string Status { get; set; } = "Reported";

        [MaxLength(1000)]
        public string? ResolutionNotes { get; set; }

        [Precision(10, 2)]
        public decimal RepairCost { get; set; } = 0m;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ResolvedAt { get; set; }
    }

    public class BookingMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public Booking Booking { get; set; } = null!;

        [Required]
        public int SenderId { get; set; }

        [ForeignKey(nameof(SenderId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Sender { get; set; } = null!;

        [Required, MaxLength(1500)]
        public string MessageText { get; set; } = "";

        public DateTime SentAt { get; set; } = DateTime.Now;
        public bool IsRead { get; set; } = false;
    }

    // =========================================================================
    // 6. REVIEW & REPUTATION MODULE
    // =========================================================================
    public class Review
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public Booking Booking { get; set; } = null!;

        [Required]
        public int PropertyId { get; set; }

        [ForeignKey(nameof(PropertyId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public Property Property { get; set; } = null!;

        [Required]
        public int GuestId { get; set; }

        [ForeignKey(nameof(GuestId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Guest { get; set; } = null!;

        // Multi-Criteria Star Ratings (1-5)
        [Range(1, 5)]
        public int CleanlinessRating { get; set; } = 5;

        [Range(1, 5)]
        public int AccuracyRating { get; set; } = 5;

        [Range(1, 5)]
        public int CommunicationRating { get; set; } = 5;

        [Range(1, 5)]
        public int LocationRating { get; set; } = 5;

        [Range(1.0, 5.0)]
        public double OverallRating { get; set; } = 5.0;

        [Required, MaxLength(1500)]
        public string Comment { get; set; } = "";

        // Mutual Review Workflow: Host can rate and feedback guest
        [Range(1, 5)]
        public int? HostRatingForGuest { get; set; }

        [MaxLength(1000)]
        public string? HostReply { get; set; }

        // Blind Review Publishing Rule: Only published when both submitted or 14-day window expires
        public bool IsPublished { get; set; } = true;

        // AJAX Helpful Votes
        public int HelpfulVotes { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // =========================================================================
    // 8. SECURITY, AUDIT & DATABASE INFRASTRUCTURE MODULE
    // =========================================================================
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        public int? UserId { get; set; }

        [MaxLength(100)]
        public string UserEmail { get; set; } = "System";

        [Required, MaxLength(100)]
        public string Action { get; set; } = "";

        [Required, MaxLength(2000)]
        public string Details { get; set; } = "";

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
