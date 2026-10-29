using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.ViewModels;

public sealed class CollectBalanceViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Required]
    public Guid SubmissionId { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal Amount { get; set; }
}
