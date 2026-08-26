namespace CourtBookingSystem.Areas.Admin.ViewModels
{
    public class AssignRolesViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<RoleSelection> Roles { get; set; } = new();
    }

    public class RoleSelection
    {
        public string RoleName { get; set; } = string.Empty;
        public bool Selected { get; set; }
    }
}