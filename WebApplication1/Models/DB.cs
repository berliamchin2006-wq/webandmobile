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
        public DbSet<RoomUnit> RoomUnits { get; set; }
        public DbSet<ViewingAppointment> ViewingAppointments { get; set; }
        public DbSet<TenancyBooking> TenancyBookings { get; set; }
        public DbSet<RentalPayment> RentalPayments { get; set; }
        public DbSet<MaintenanceTicket> MaintenanceTickets { get; set; }
    }

    [Index(nameof(Email), IsUnique = true)]
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Email { get; set; } = "";

        [Required, MaxLength(255)]
        public string PasswordHash { get; set; } = "";

        [Required, MaxLength(100)]
        public string FullName { get; set; } = "";

        [Required, MaxLength(20)]
        public string PhoneNumber { get; set; } = "";

        [Required, MaxLength(20)]
        public string Role { get; set; } = "Tenant";

        [MaxLength(255)]
        public string ProfilePhoto { get; set; } = "default.png";

        public bool IsEmailVerified { get; set; } = false;

        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }

        [MaxLength(100)]
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [InverseProperty(nameof(Property.Owner))]
        public List<Property> OwnedProperties { get; set; } = [];
    }

    public class Property
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OwnerId { get; set; }

        [ForeignKey(nameof(OwnerId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Owner { get; set; } = null!;

        [Required, MaxLength(150)]
        public string Title { get; set; } = "";

        [Required, MaxLength(50)]
        public string PropertyType { get; set; } = "";

        [Required, MaxLength(255)]
        public string Address { get; set; } = "";

        [Required, MaxLength(50)]
        public string City { get; set; } = "";

        [Required, MaxLength(50)]
        public string State { get; set; } = "";

        [Required, MaxLength(10)]
        public string Postcode { get; set; } = "";

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [Required, MaxLength(2000)]
        public string Description { get; set; } = "";

        [MaxLength(500)]
        public string AmenitiesCsv { get; set; } = "";

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<PropertyPhoto> Photos { get; set; } = [];
        public List<RoomUnit> RoomUnits { get; set; } = [];
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
    }

    public class RoomUnit
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PropertyId { get; set; }

        [ForeignKey(nameof(PropertyId))]
        public Property Property { get; set; } = null!;

        [Required, MaxLength(100)]
        public string RoomName { get; set; } = "";

        [Required, MaxLength(50)]
        public string RoomType { get; set; } = "";

        [Precision(10, 2)]
        public decimal MonthlyRent { get; set; }

        [Precision(10, 2)]
        public decimal SecurityDeposit { get; set; }

        [Precision(10, 2)]
        public decimal UtilityDeposit { get; set; }

        public int MaxPax { get; set; } = 1;
        public bool IsAvailable { get; set; } = true;

        public List<ViewingAppointment> ViewingAppointments { get; set; } = [];
        public List<TenancyBooking> TenancyBookings { get; set; } = [];
    }

    public class ViewingAppointment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RoomUnitId { get; set; }

        [ForeignKey(nameof(RoomUnitId))]
        public RoomUnit RoomUnit { get; set; } = null!;

        [Required]
        public int TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Tenant { get; set; } = null!;

        [Required]
        public DateTime AppointmentDateTime { get; set; }

        [Required, MaxLength(30)]
        public string Status { get; set; } = "Pending";

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [MaxLength(100)]
        public string QrCodeToken { get; set; } = Guid.NewGuid().ToString("N");
    }

    public class TenancyBooking
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RoomUnitId { get; set; }

        [ForeignKey(nameof(RoomUnitId))]
        public RoomUnit RoomUnit { get; set; } = null!;

        [Required]
        public int TenantId { get; set; }

        [ForeignKey(nameof(TenantId))]
        [DeleteBehavior(DeleteBehavior.Restrict)]
        public User Tenant { get; set; } = null!;

        [Required, DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required, DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        public int DurationMonths { get; set; }

        [Precision(10, 2)]
        public decimal AgreedMonthlyRent { get; set; }

        [Precision(10, 2)]
        public decimal TotalDepositPaid { get; set; }

        [Required, MaxLength(30)]
        public string Status { get; set; } = "PendingApproval";

        public DateTime BookingDate { get; set; } = DateTime.Now;

        public List<RentalPayment> Payments { get; set; } = [];
        public List<MaintenanceTicket> MaintenanceTickets { get; set; } = [];
    }

    public class RentalPayment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TenancyBookingId { get; set; }

        [ForeignKey(nameof(TenancyBookingId))]
        public TenancyBooking TenancyBooking { get; set; } = null!;

        [Required, MaxLength(50)]
        public string PaymentType { get; set; } = "MonthlyRent";

        [Required, MaxLength(20)]
        public string BillingMonth { get; set; } = "";

        [Precision(10, 2)]
        public decimal Amount { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [Required, MaxLength(50)]
        public string PaymentMethod { get; set; } = "Stripe / FPX";

        [Required, MaxLength(100)]
        public string TransactionRef { get; set; } = "";

        [Required, MaxLength(30)]
        public string Status { get; set; } = "Paid";
    }

    public class MaintenanceTicket
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TenancyBookingId { get; set; }

        [ForeignKey(nameof(TenancyBookingId))]
        public TenancyBooking TenancyBooking { get; set; } = null!;

        [Required, MaxLength(150)]
        public string IssueTitle { get; set; } = "";

        [Required, MaxLength(1000)]
        public string Description { get; set; } = "";

        [Required, MaxLength(20)]
        public string Priority { get; set; } = "Medium";

        [MaxLength(255)]
        public string? PhotoProof { get; set; }

        [Required, MaxLength(30)]
        public string Status { get; set; } = "Reported";

        [Precision(10, 2)]
        public decimal RepairCost { get; set; } = 0m;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}