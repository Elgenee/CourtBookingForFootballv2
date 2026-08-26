using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models
{
    /// <summary>
    /// Groups together courts that share the same physical playing area.
    /// Members of a group constrain each other's availability via the
    /// IsFullCourt flag on <see cref="Court"/>:
    ///
    ///   - If a Full Court in the group has a booking at time T, every
    ///     other court in the group (Full or Split) is unavailable at T.
    ///   - If a Split Court in the group has a booking at time T, only
    ///     the Full Court(s) in the group are unavailable — other Split
    ///     Courts remain bookable.
    ///
    /// Courts with a null <c>CourtGroupId</c> are independent (no sharing).
    /// </summary>
    public class CourtGroup
    {
        public int Id { get; set; }

        [Required]
        [StringLength(40)]
        [Display(Name = "Group Code")]
        public string GroupCode { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Group Name")]
        public string GroupName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        // Navigation
        public ICollection<Court> Courts { get; set; } = new List<Court>();
    }
}
