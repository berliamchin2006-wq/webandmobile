using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HomeNest.Models
{
    // =========================================================================
    // 1. USER & IDENTITY VIEW MODELS
    // =========================================================================
    public class LoginVM
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        [MaxLength(100)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }

        [Required(ErrorMessage = "Verification code is required.")]
        [StringLength(6, MinimumLength = 4, ErrorMessage = "Invalid verification code length.")]
        public string Captcha { get; set; } = "";
    }

    public class RegisterVM
    {
        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [MaxLength(100)]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 100 characters.")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Malaysian phone number is required.")]
        [RegularExpression(@"^01[0-9]-?\d{7,8}$", ErrorMessage = "Format must be 01X-XXXXXXX (e.g., 012-3456789).")]
        public string PhoneNumber { get; set; } = "";

        [Required(ErrorMessage = "Please select whether you want to book or host.")]
        [RegularExpression(@"^(Guest|Host)$", ErrorMessage = "Role must be either Guest or Host.")]
        public string Role { get; set; } = "Guest";

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(50, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Confirm password is required.")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";

        [Required(ErrorMessage = "Verification code is required.")]
        public string Captcha { get; set; } = "";
    }

    public class ForgotPasswordVM
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Verification code is required.")]
        public string Captcha { get; set; } = "";
    }

    public class ResetPasswordVM
    {
        [Required]
        public string Token { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(50, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Confirm password is required.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }

    public class ProfileVM
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100, MinimumLength = 3)]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Phone number is required.")]
        public string PhoneNumber { get; set; } = "";

        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public string CurrentPhoto { get; set; } = "default.png";

        public IFormFile? PhotoUpload { get; set; }

        // Live Webcam Capture Base64 Data
        public string? WebcamPhotoData { get; set; }

        // Password Change
        [DataType(DataType.Password)]
        public string? CurrentPassword { get; set; }

        [DataType(DataType.Password)]
        [StringLength(50, MinimumLength = 8, ErrorMessage = "New password must be at least 8 characters.")]
        public string? NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "New passwords do not match.")]
        public string? ConfirmNewPassword { get; set; }

        // UI Preferences
        public string ThemePreference { get; set; } = "light";
        public string PreferredCurrency { get; set; } = "MYR";

        // Host Payout Details
        public string? BankName { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? BankHolderName { get; set; }

        // Host ID Verification Status
        public string IdentityStatus { get; set; } = "Unverified";
        public string? IdDocumentPhoto { get; set; }
        public string? IdentityNotes { get; set; }
    }

    public class HostVerificationSubmissionVM
    {
        [Required(ErrorMessage = "Please upload an identity document image (IC / Passport / License).")]
        public IFormFile IdDocument { get; set; } = null!;

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    // =========================================================================
    // 2. PROPERTY & LISTING VIEW MODELS
    // =========================================================================
    public class PropertyFormVM
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Listing title is required.")]
        [StringLength(150, MinimumLength = 5, ErrorMessage = "Title must be between 5 and 150 characters.")]
        public string Title { get; set; } = "";

        [Required(ErrorMessage = "Please select a property type.")]
        public string PropertyType { get; set; } = "Condominium";

        [Required(ErrorMessage = "Street address is required.")]
        [MaxLength(255)]
        public string Address { get; set; } = "";

        [Required(ErrorMessage = "City is required.")]
        [MaxLength(50)]
        public string City { get; set; } = "";

        [Required(ErrorMessage = "State is required.")]
        [MaxLength(50)]
        public string State { get; set; } = "Kuala Lumpur";

        [Required(ErrorMessage = "5-digit postcode is required.")]
        [RegularExpression(@"^\d{5}$", ErrorMessage = "Postcode must be 5 digits.")]
        public string Postcode { get; set; } = "";

        // Interactive Map Coordinates
        [Range(-90.0, 90.0, ErrorMessage = "Invalid latitude.")]
        public double Latitude { get; set; } = 3.1390;

        [Range(-180.0, 180.0, ErrorMessage = "Invalid longitude.")]
        public double Longitude { get; set; } = 101.6869;

        [Required(ErrorMessage = "Description is required.")]
        [StringLength(2500, MinimumLength = 20, ErrorMessage = "Description must be at least 20 characters.")]
        public string Description { get; set; } = "";

        // Space & Guests
        [Range(1, 30, ErrorMessage = "Guest capacity must be between 1 and 30.")]
        public int MaxGuests { get; set; } = 2;

        [Range(1, 20)]
        public int Bedrooms { get; set; } = 1;

        [Range(1, 30)]
        public int Beds { get; set; } = 1;

        [Range(1, 10)]
        public int Bathrooms { get; set; } = 1;

        // Pricing
        [Required(ErrorMessage = "Base nightly rate is required.")]
        [Range(20, 20000, ErrorMessage = "Nightly rate must be between RM 20 and RM 20,000.")]
        public decimal BaseNightlyRate { get; set; } = 150m;

        [Required]
        [Range(0, 2000, ErrorMessage = "Cleaning fee must be between RM 0 and RM 2,000.")]
        public decimal CleaningFee { get; set; } = 50m;

        [Required]
        [Range(20, 25000, ErrorMessage = "Weekend rate must be between RM 20 and RM 25,000.")]
        public decimal CustomWeekendRate { get; set; } = 180m;

        // Policies & Rules
        [Required]
        public string CancellationPolicy { get; set; } = "Flexible";

        [MaxLength(1000)]
        public string HouseRules { get; set; } = "Check-in after 3:00 PM. Check-out by 11:00 AM. No smoking. No parties or events. Quiet hours 10:00 PM - 8:00 AM.";

        public List<string> SelectedAmenities { get; set; } = [];

        // Multi-Photo Upload & Existing Photos
        public List<IFormFile>? NewPhotos { get; set; }
        public List<PropertyPhoto>? ExistingPhotos { get; set; }
        public int? SelectedCoverPhotoId { get; set; }
    }

    public class BatchPricingImportVM
    {
        [Required(ErrorMessage = "Please select a CSV file to upload.")]
        public IFormFile CsvFile { get; set; } = null!;
    }

    // =========================================================================
    // 3. BOOKING & CHECKOUT VIEW MODELS
    // =========================================================================
    public class BookingCheckoutVM
    {
        [Required]
        public int PropertyId { get; set; }

        public Property? Property { get; set; }

        [Required(ErrorMessage = "Check-in date is required.")]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; } = DateTime.Today.AddDays(1);

        [Required(ErrorMessage = "Check-out date is required.")]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; } = DateTime.Today.AddDays(3);

        [Range(1, 30, ErrorMessage = "Number of guests must be at least 1.")]
        public int GuestCount { get; set; } = 1;

        [MaxLength(500)]
        public string? SpecialRequests { get; set; }

        // Calculations
        public int TotalNights { get; set; }
        public decimal NightlyRate { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal CleaningFee { get; set; }
        public decimal ServiceFee { get; set; }
        public decimal TotalAmount { get; set; }

        // Payment Options: "Stripe (Mock Card)", "FPX Online Banking", "Touch 'n Go eWallet"
        [Required(ErrorMessage = "Please select a payment method.")]
        public string PaymentMethod { get; set; } = "Stripe (Mock Card)";

        // Mock Card Fields
        public string? CardNumber { get; set; }
        public string? CardExpiry { get; set; }
        public string? CardCvc { get; set; }
        public string? CardHolder { get; set; }

        // Mock FPX Bank
        public string? FpxBank { get; set; }
    }

    // =========================================================================
    // 5. IN-STAY & MAINTENANCE VIEW MODELS
    // =========================================================================
    public class MaintenanceRequestVM
    {
        [Required]
        public int BookingId { get; set; }

        [Required(ErrorMessage = "Please select an issue category.")]
        public string Category { get; set; } = "Air-Conditioning";

        [Required(ErrorMessage = "Issue summary is required.")]
        [StringLength(150, MinimumLength = 5)]
        public string IssueTitle { get; set; } = "";

        [Required(ErrorMessage = "Detailed description is required.")]
        [StringLength(1500, MinimumLength = 10)]
        public string Description { get; set; } = "";

        [Required]
        public string Priority { get; set; } = "Medium";

        public IFormFile? PhotoProof { get; set; }
    }

    // =========================================================================
    // 6. REVIEW & REPUTATION VIEW MODELS
    // =========================================================================
    public class ReviewFormVM
    {
        [Required]
        public int BookingId { get; set; }

        public int PropertyId { get; set; }

        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5 stars.")]
        public int CleanlinessRating { get; set; } = 5;

        [Range(1, 5)]
        public int AccuracyRating { get; set; } = 5;

        [Range(1, 5)]
        public int CommunicationRating { get; set; } = 5;

        [Range(1, 5)]
        public int LocationRating { get; set; } = 5;

        [Required(ErrorMessage = "Please share your stay experience.")]
        [StringLength(1500, MinimumLength = 10, ErrorMessage = "Review must be at least 10 characters.")]
        public string Comment { get; set; } = "";

        // Host review of guest
        [Range(1, 5)]
        public int? HostRatingForGuest { get; set; }

        [MaxLength(1000)]
        public string? HostReply { get; set; }
    }
}
