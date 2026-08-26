namespace CourtBookingSystem.Areas.Admin.ViewModels
{
    public class UserListItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public bool EmailConfirmed { get; set; }
        public bool LockedOut { get; set; }
        public string Roles { get; set; } = string.Empty;
    }
}