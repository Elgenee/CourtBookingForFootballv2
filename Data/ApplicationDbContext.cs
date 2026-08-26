using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Cms;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Court> Courts => Set<Court>();
        public DbSet<CourtGroup> CourtGroups => Set<CourtGroup>();
        public DbSet<Booking> Bookings => Set<Booking>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
        public DbSet<BookingPricingRule> BookingPricingRules => Set<BookingPricingRule>();
        public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
        public DbSet<Notification> Notifications => Set<Notification>();

        // ----- CMS / Website Settings -----
        public DbSet<WebsiteSetting> WebsiteSettings => Set<WebsiteSetting>();
        public DbSet<HeroImage> HeroImages => Set<HeroImage>();
        public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
        public DbSet<AboutContent> AboutContents => Set<AboutContent>();
        public DbSet<Promotion> Promotions => Set<Promotion>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Booking -> User (optional, guest bookings allowed)
            builder.Entity<Booking>()
                .HasOne(b => b.User)
                .WithMany(u => u.Bookings)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Booking -> Court
            builder.Entity<Booking>()
                .HasOne(b => b.Court)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Restrict);

            // Booking reference unique
            builder.Entity<Booking>()
                .HasIndex(b => b.BookingReferenceNo)
                .IsUnique();

            // Payment -> Booking
            builder.Entity<Payment>()
                .HasOne(p => p.Booking)
                .WithMany(b => b.Payments)
                .HasForeignKey(p => p.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // Payment -> ConfirmedByUser (optional)
            builder.Entity<Payment>()
                .HasOne(p => p.ConfirmedByUser)
                .WithMany()
                .HasForeignKey(p => p.ConfirmedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // BlockedSlot -> Court
            builder.Entity<BlockedSlot>()
                .HasOne(s => s.Court)
                .WithMany(c => c.BlockedSlots)
                .HasForeignKey(s => s.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            // BusinessHour unique per day
            builder.Entity<BusinessHour>()
                .HasIndex(b => b.DayOfWeek)
                .IsUnique();

            builder.Entity<BookingPricingRule>()
                .HasOne(r => r.Court)
                .WithMany()
                .HasForeignKey(r => r.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BookingPricingRule>()
                .HasIndex(r => new { r.CourtId, r.IsPromotional, r.IsEnabled });

            // ----- CourtGroup -----
            builder.Entity<CourtGroup>()
                .HasIndex(g => g.GroupCode)
                .IsUnique();

            // Court -> CourtGroup (optional). Deleting a group nulls the FK so
            // courts survive as independent (sharing-free) entities.
            builder.Entity<Court>()
                .HasOne(c => c.CourtGroup)
                .WithMany(g => g.Courts)
                .HasForeignKey(c => c.CourtGroupId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
