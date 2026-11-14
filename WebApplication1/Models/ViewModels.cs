using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace HomeNest.Models
{
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
        [Remote(action: "CheckEmail", controller: "Account", ErrorMessage = "Email is already registered.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 100 characters.")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Malaysian phone number is required.")]
        [RegularExpression(@"^01[0-9]-\d{7,8}$", ErrorMessage = "Format must be 01X-XXXXXXX (e.g., 012-3456789).")]
        public string PhoneNumber { get; set; } = "";

        [Required(ErrorMessage = "Please select an account role.")]
        [RegularExpression(@"^(Owner|Tenant)$", ErrorMessage = "Role must be either Owner or Tenant.")]
        public string Role { get; set; } = "Tenant";

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(50, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Confirm password is required.")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }

    public class PropertyFormVM
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Property title is required.")]
        [StringLength(150, MinimumLength = 5)]
        public string Title { get; set; } = "";

        [Required(ErrorMessage = "Please select a property type.")]
        public string PropertyType { get; set; } = "Condominium";

        [Required(ErrorMessage = "Street address is required.")]
        [MaxLength(255)]
        public string Address { get; set; } = "";

        [Required]
        [MaxLength(50)]
        public string City { get; set; } = "";

        [Required]
        [MaxLength(50)]
        public string State { get; set; } = "Kuala Lumpur";

        [Required]
        [RegularExpression(@"^\d{5}$", ErrorMessage = "Postcode must be 5 digits.")]
        public string Postcode { get; set; } = "";

        [Range(-90.0, 90.0, ErrorMessage = "Invalid latitude.")]
        public double Latitude { get; set; } = 3.2015;

        [Range(-180.0, 180.0, ErrorMessage = "Invalid longitude.")]
        public double Longitude { get; set; } = 101.7168;

        [Required]
        [StringLength(2000, MinimumLength = 20, ErrorMessage = "Description must be at least 20 characters.")]
        public string Description { get; set; } = "";

        public List<string> SelectedAmenities { get; set; } = [];

        public List<IFormFile>? Photos { get; set; }
    }

    public class RoomUnitFormVM
    {
        [Required]
        public int PropertyId { get; set; }

        [Required, StringLength(100)]
        public string RoomName { get; set; } = "";

        [Required, StringLength(50)]
        public string RoomType { get; set; } = "Single Room";

        [Required]
        [Range(100, 20000, ErrorMessage = "Monthly rent must be between RM 100 and RM 20,000.")]
        public decimal MonthlyRent { get; set; }

        [Required]
        [Range(0, 50000, ErrorMessage = "Security deposit must be a positive value.")]
        public decimal SecurityDeposit { get; set; }

        [Required]
        [Range(0, 10000, ErrorMessage = "Utility deposit must be a positive value.")]
        public decimal UtilityDeposit { get; set; }

        [Range(1, 10, ErrorMessage = "Maximum occupancy must be between 1 and 10 pax.")]
        public int MaxPax { get; set; } = 1;
    }

    public class TenancyBookingVM
    {
        [Required]
        public int RoomUnitId { get; set; }

        [Required(ErrorMessage = "Move-in start date is required.")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; } = DateTime.Today.AddDays(7);

        [Required(ErrorMessage = "Lease duration is required.")]
        [Range(3, 36, ErrorMessage = "Minimum rental tenure is 3 months, maximum is 36 months.")]
        public int DurationMonths { get; set; } = 12;
    }
}