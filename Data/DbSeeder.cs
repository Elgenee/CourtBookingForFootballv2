using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Cms;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Data
{
    public static class DbSeeder
    {
        public static class Roles
        {
            public const string SuperAdmin = "SuperAdmin";
            public const string Admin = "Admin";
            public const string Staff = "Staff";
            public const string Member = "Member";
        }

        public static async Task SeedAsync(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            var db = services.GetRequiredService<ApplicationDbContext>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            if (db.Database.IsSqlite())
            {
                await db.Database.EnsureCreatedAsync();
                // SQLite path uses EnsureCreated (no migrations table), so
                // re-run the status remap idempotently against any legacy data
                // left over from the pre-simplification workflow.
                await RemapLegacyStatusesAsync(db);
                await EnsureSqliteCmsSchemaAsync(db);
            }
            else
            {
                await db.Database.MigrateAsync();
            }

            await EnsureBookingPricingSchemaAsync(db);

            await SeedRolesAsync(roleManager);
            await SeedDefaultSuperAdminAsync(userManager, configuration);
            await SeedDefaultAdminAsync(userManager, configuration);
            await SeedDefaultStaffAsync(userManager, configuration);
            await SeedBusinessHoursAsync(db);
            await SeedCourtsAsync(db);
            await SeedDefaultBookingPricingRulesAsync(db);
            await SeedCourtGroupsAsync(db);

            // CMS seeds
            await SeedWebsiteSettingsAsync(db);
            await SeedAboutContentAsync(db);
            await SeedHeroImagesAsync(db);
            await SeedGalleryImagesAsync(db);
            await SeedPromotionsAsync(db);
        }

        private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
        {
            string[] roles = { Roles.SuperAdmin, Roles.Admin, Roles.Staff, Roles.Member };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }
        }

        // Idempotent remap of legacy enum int values into the simplified workflow.
        // Used on the SQLite EnsureCreated path (where EF migrations don't run).
        // Matches the logic in 20260602063500_SimplifyBookingPaymentStatuses.cs.
        private static async Task RemapLegacyStatusesAsync(ApplicationDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE Bookings
                SET BookingStatus = CASE BookingStatus
                    WHEN 4 THEN 3   -- CheckedIn  -> Confirmed
                    WHEN 5 THEN 4   -- Completed  -> Completed
                    WHEN 6 THEN 5   -- Cancelled  -> Cancelled
                    WHEN 7 THEN 5   -- NoShow     -> Cancelled
                    ELSE BookingStatus
                END
                WHERE BookingStatus > 3;");

            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE Payments SET PaymentStatus = 4 WHERE PaymentStatus = 5;");
        }

        private static async Task SeedDefaultSuperAdminAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            var email = configuration["DefaultSuperAdmin:Email"] ?? "superadmin@courtbooking.com";
            var password = configuration["DefaultSuperAdmin:Password"] ?? "SuperAdmin@123";
            var fullName = configuration["DefaultSuperAdmin:FullName"] ?? "Super Administrator";

            if (await userManager.FindByEmailAsync(email) != null) return;

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, Roles.SuperAdmin);
                // SuperAdmin also receives Admin-level operational access by default.
                await userManager.AddToRoleAsync(user, Roles.Admin);
            }
        }

        private static async Task SeedDefaultAdminAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            var adminEmail = configuration["DefaultAdmin:Email"] ?? "admin@courtbooking.com";
            var adminPassword = configuration["DefaultAdmin:Password"] ?? "Admin@123";
            var adminFullName = configuration["DefaultAdmin:FullName"] ?? "System Administrator";

            if (await userManager.FindByEmailAsync(adminEmail) != null) return;

            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                FullName = adminFullName,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Admin);
            }
        }

        private static async Task SeedDefaultStaffAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            var email = configuration["DefaultStaff:Email"] ?? "staff@courtbooking.com";
            var password = configuration["DefaultStaff:Password"] ?? "Staff@123";
            var fullName = configuration["DefaultStaff:FullName"] ?? "Front Desk Staff";

            if (await userManager.FindByEmailAsync(email) != null) return;

            var staff = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(staff, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(staff, Roles.Staff);
            }
        }

        private static async Task SeedBusinessHoursAsync(ApplicationDbContext db)
        {
            if (await db.BusinessHours.AnyAsync()) return;

            var open = new TimeSpan(8, 0, 0);
            var close = new TimeSpan(22, 0, 0);

            var hours = Enum.GetValues<DayOfWeek>()
                .Select(d => new BusinessHour
                {
                    DayOfWeek = d,
                    OpenTime = open,
                    CloseTime = close,
                    IsClosed = false
                });

            await db.BusinessHours.AddRangeAsync(hours);
            await db.SaveChangesAsync();
        }

        private static async Task SeedCourtsAsync(ApplicationDbContext db)
        {
            if (await db.Courts.AnyAsync()) return;

            var courts = new List<Court>
            {
                // MAIN_COURT — convertible Basketball / Pickleball 1-3
                new() { CourtName = "Basketball Court",   SportType = SportType.Basketball, Description = "Main convertible full-size basketball court", HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true,  IsFullCourt = true  },
                new() { CourtName = "Pickleball Court 1", SportType = SportType.Pickleball, Description = "Split court on the main convertible area",     HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true,  IsFullCourt = false },
                new() { CourtName = "Pickleball Court 2", SportType = SportType.Pickleball, Description = "Split court on the main convertible area",     HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true,  IsFullCourt = false },
                new() { CourtName = "Pickleball Court 3", SportType = SportType.Pickleball, Description = "Split court on the main convertible area",     HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true,  IsFullCourt = false },

                // PB4 — standalone pickleball court
                new() { CourtName = "Pickleball Court 4", SportType = SportType.Pickleball, Description = "Independent pickleball court",                  HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true,  IsFullCourt = false },

                // Independent (no group)
                //new() { CourtName = "Badminton Court 1",  SportType = SportType.Badminton,  Description = "Standard badminton court", HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true },
                //new() { CourtName = "Tennis Court 1",     SportType = SportType.Tennis,     Description = "Outdoor tennis court",     HourlyRate = 0m, Status = CourtStatus.Available, IsActive = true }
            };

            await db.Courts.AddRangeAsync(courts);
            await db.SaveChangesAsync();
        }

        private static async Task SeedDefaultBookingPricingRulesAsync(ApplicationDbContext db)
        {
            var courtIdsWithRules = await db.BookingPricingRules
                .Select(r => r.CourtId)
                .Distinct()
                .ToListAsync();

            var courtsWithoutRules = await db.Courts
                .Where(c => !courtIdsWithRules.Contains(c.Id))
                .OrderBy(c => c.CourtName)
                .ToListAsync();

            if (courtsWithoutRules.Count == 0) return;

            var rules = new List<BookingPricingRule>();
            foreach (var court in courtsWithoutRules)
            {
                rules.AddRange(new[]
                {
                    new BookingPricingRule
                    {
                        CourtId = court.Id,
                        Name = "Normal rate",
                        IsPromotional = false,
                        IsEnabled = true,
                        StartTime = new TimeSpan(5, 0, 0),
                        EndTime = new TimeSpan(16, 0, 0),
                        HourlyRate = 1800m,
                        DisplayOrder = 0
                    },
                    new BookingPricingRule
                    {
                        CourtId = court.Id,
                        Name = "Normal rate",
                        IsPromotional = false,
                        IsEnabled = true,
                        StartTime = new TimeSpan(16, 0, 0),
                        EndTime = new TimeSpan(18, 0, 0),
                        HourlyRate = 2000m,
                        DisplayOrder = 1
                    },
                    new BookingPricingRule
                    {
                        CourtId = court.Id,
                        Name = "Normal rate",
                        IsPromotional = false,
                        IsEnabled = true,
                        StartTime = new TimeSpan(18, 0, 0),
                        EndTime = TimeSpan.Zero,
                        HourlyRate = 2800m,
                        DisplayOrder = 2
                    }
                });
            }

            await db.BookingPricingRules.AddRangeAsync(rules);
            await db.SaveChangesAsync();
        }

        // Seeds the sample MAIN_COURT and PB4 groups described in the spec,
        // and idempotently assigns matching seed courts to them. Safe to run
        // repeatedly — only inserts groups that are missing and only assigns
        // courts that currently have no group.
        private static async Task SeedCourtGroupsAsync(ApplicationDbContext db)
        {
            var mainGroup = await db.CourtGroups.FirstOrDefaultAsync(g => g.GroupCode == "MAIN_COURT");
            if (mainGroup == null)
            {
                mainGroup = new CourtGroup
                {
                    GroupCode = "MAIN_COURT",
                    GroupName = "Main Convertible Court",
                    Description = "Basketball + Pickleball 1-3 share the same physical area.",
                    IsActive = true
                };
                db.CourtGroups.Add(mainGroup);
            }

            var pb4Group = await db.CourtGroups.FirstOrDefaultAsync(g => g.GroupCode == "PB4");
            if (pb4Group == null)
            {
                pb4Group = new CourtGroup
                {
                    GroupCode = "PB4",
                    GroupName = "Pickleball Court 4",
                    Description = "Independent pickleball court (single-member group).",
                    IsActive = true
                };
                db.CourtGroups.Add(pb4Group);
            }

            await db.SaveChangesAsync();

            // Assign known seed courts to the matching group only if they
            // aren't already in a group — never overwrite admin-managed config.
            string[] mainCourtNames = { "Basketball Court", "Pickleball Court 1", "Pickleball Court 2", "Pickleball Court 3" };
            var mainCourts = await db.Courts
                .Where(c => mainCourtNames.Contains(c.CourtName) && c.CourtGroupId == null)
                .ToListAsync();
            foreach (var c in mainCourts)
            {
                c.CourtGroupId = mainGroup.Id;
                if (c.CourtName == "Basketball Court") c.IsFullCourt = true;
            }

            var pb4Court = await db.Courts
                .FirstOrDefaultAsync(c => c.CourtName == "Pickleball Court 4" && c.CourtGroupId == null);
            if (pb4Court != null)
            {
                pb4Court.CourtGroupId = pb4Group.Id;
            }

            await db.SaveChangesAsync();
        }

        // ---------------- CMS SEEDS ----------------

        private static async Task EnsureSqliteCmsSchemaAsync(ApplicationDbContext db)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE WebsiteSettings ADD COLUMN LandingColorScheme TEXT NOT NULL DEFAULT 'royal-blue';");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
                                             ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Existing database already has the CMS theme column.
            }

            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS Promotions (
                    Id INTEGER NOT NULL CONSTRAINT PK_Promotions PRIMARY KEY AUTOINCREMENT,
                    AnnouncementType TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    ImagePath TEXT NOT NULL,
                    ImageAlt TEXT NULL,
                    ButtonLabel TEXT NOT NULL,
                    ButtonUrl TEXT NOT NULL,
                    IconHtml TEXT NOT NULL,
                    StartDate TEXT NULL,
                    EndDate TEXT NULL,
                    DisplayOrder INTEGER NOT NULL,
                    IsActive INTEGER NOT NULL,
                    UpdatedDate TEXT NOT NULL
                );");
        }

        private static async Task EnsureBookingPricingSchemaAsync(ApplicationDbContext db)
        {
            if (db.Database.IsSqlite())
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE TABLE IF NOT EXISTS BookingPricingRules (
                        Id INTEGER NOT NULL CONSTRAINT PK_BookingPricingRules PRIMARY KEY AUTOINCREMENT,
                        CourtId INTEGER NOT NULL,
                        Name TEXT NOT NULL,
                        IsPromotional INTEGER NOT NULL,
                        IsEnabled INTEGER NOT NULL,
                        EffectiveStartDate TEXT NULL,
                        EffectiveEndDate TEXT NULL,
                        StartTime TEXT NOT NULL,
                        EndTime TEXT NOT NULL,
                        HourlyRate TEXT NOT NULL,
                        Priority INTEGER NOT NULL,
                        DisplayOrder INTEGER NOT NULL,
                        CONSTRAINT FK_BookingPricingRules_Courts_CourtId
                            FOREIGN KEY (CourtId) REFERENCES Courts (Id) ON DELETE CASCADE
                    );");

                await db.Database.ExecuteSqlRawAsync(@"
                    CREATE INDEX IF NOT EXISTS IX_BookingPricingRules_CourtId_IsPromotional_IsEnabled
                    ON BookingPricingRules (CourtId, IsPromotional, IsEnabled);");
                return;
            }

            await db.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID(N'[BookingPricingRules]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BookingPricingRules] (
                        [Id] int NOT NULL IDENTITY,
                        [CourtId] int NOT NULL,
                        [Name] nvarchar(100) NOT NULL,
                        [IsPromotional] bit NOT NULL,
                        [IsEnabled] bit NOT NULL,
                        [EffectiveStartDate] datetime2 NULL,
                        [EffectiveEndDate] datetime2 NULL,
                        [StartTime] time NOT NULL,
                        [EndTime] time NOT NULL,
                        [HourlyRate] decimal(18,2) NOT NULL,
                        [Priority] int NOT NULL,
                        [DisplayOrder] int NOT NULL,
                        CONSTRAINT [PK_BookingPricingRules] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_BookingPricingRules_Courts_CourtId]
                            FOREIGN KEY ([CourtId]) REFERENCES [Courts] ([Id]) ON DELETE CASCADE
                    );
                END");

            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_BookingPricingRules_CourtId_IsPromotional_IsEnabled'
                        AND object_id = OBJECT_ID(N'[BookingPricingRules]')
                )
                BEGIN
                    CREATE INDEX [IX_BookingPricingRules_CourtId_IsPromotional_IsEnabled]
                    ON [BookingPricingRules] ([CourtId], [IsPromotional], [IsEnabled]);
                END");
        }

        private static async Task SeedWebsiteSettingsAsync(ApplicationDbContext db)
        {
            if (await db.WebsiteSettings.AnyAsync()) return;

            db.WebsiteSettings.Add(new WebsiteSetting
            {
                WebsiteName = "Royal Court",
                WebsiteTagline = "Premium Sports Center",
                NavbarLogoPath = null,
                LandingColorScheme = LandingColorSchemeCatalog.DefaultKey,
                HeroTitle = "Book Your Next Game",
                HeroSubtitle = "Premium courts for Pickleball, Badminton, Basketball and Tennis — book online in seconds or just walk in.",
                HeroButtonText = "Find Available Times",
                UpdatedDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        private static async Task SeedAboutContentAsync(ApplicationDbContext db)
        {
            if (await db.AboutContents.AnyAsync()) return;

            db.AboutContents.Add(new AboutContent
            {
                Title = "About Royal Court",
                Description = "Royal Court is a modern sports center built for players of all skill levels. " +
                              "Whether you're booking for casual games, training sessions, friendly matches, or competitive play, " +
                              "Royal Court provides premium courts, flexible schedules, and a simple online booking experience.",
                FacilitiesList = string.Join('\n', new[]
                {
                    "Premium indoor courts",
                    "Clean changing rooms",
                    "Comfortable waiting area",
                    "Parking area",
                    "Equipment rental",
                    "Friendly staff",
                    "Online & walk-in booking"
                }),
                ContactPhone = "+63 917 123 4567",
                ContactEmail = "hello@royalcourt.ph",
                Location = "123 Sports Avenue, Makati City, Philippines",
                OpeningHoursDays = "Mon – Sun",
                OpeningHoursTimes = "8:00 AM – 10:00 PM",
                UpdatedDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        private static async Task SeedHeroImagesAsync(ApplicationDbContext db)
        {
            if (await db.HeroImages.AnyAsync()) return;

            // Provide 3 sample hero images using Unsplash sport photography.
            // SuperAdmin can upload their own later — these are starter assets.
            var samples = new[]
            {
                new HeroImage
                {
                    Title = "Professional Pickleball Court",
                    ImagePath = "https://images.unsplash.com/photo-1554068865-24cecd4e34b8?auto=format&fit=crop&w=1920&q=80",
                    IsDefault = true,
                    IsActive = true
                },
                new HeroImage
                {
                    Title = "Premium Badminton Court",
                    ImagePath = "https://images.unsplash.com/photo-1626224583764-f87db24ac4ea?auto=format&fit=crop&w=1920&q=80",
                    IsDefault = false,
                    IsActive = true
                },
                new HeroImage
                {
                    Title = "Mixed Sports Facility",
                    ImagePath = "https://images.unsplash.com/photo-1546519638-68e109498ffc?auto=format&fit=crop&w=1920&q=80",
                    IsDefault = false,
                    IsActive = true
                }
            };

            await db.HeroImages.AddRangeAsync(samples);
            await db.SaveChangesAsync();
        }

        private static async Task SeedGalleryImagesAsync(ApplicationDbContext db)
        {
            if (await db.GalleryImages.AnyAsync()) return;

            var items = new[]
            {
                ("Pickleball Court", "Premium indoor pickleball court designed for casual and competitive play.", "https://picsum.photos/seed/pickleball-court/640/420"),
                ("Badminton Court", "Pro-grade synthetic flooring and tournament-standard lighting.", "https://picsum.photos/seed/badminton-court/640/420"),
                ("Basketball Court", "Full-size hardwood court with adjustable rims.", "https://picsum.photos/seed/basketball-court/640/420"),
                ("Tennis Court", "Outdoor hard-court surface with professional net systems.", "https://picsum.photos/seed/tennis-court/640/420"),
                ("Lounge Area", "Relax and refuel between sessions in our cozy lounge.", "https://picsum.photos/seed/lounge-area/640/420"),
                ("Reception Area", "Friendly check-in desk and gear rental counter.", "https://picsum.photos/seed/reception-area/640/420"),
                ("Training Session", "Coaching and skill clinics for all ability levels.", "https://picsum.photos/seed/training-session/640/420"),
                ("Tournament Event", "Where champions are made — our court hosts regular tournaments.", "https://picsum.photos/seed/tournament-event/640/420")
            };

            var order = 0;
            foreach (var (title, caption, url) in items)
            {
                db.GalleryImages.Add(new GalleryImage
                {
                    Title = title,
                    Caption = caption,
                    ImagePath = url,
                    DisplayOrder = order++,
                    IsActive = true
                });
            }
            await db.SaveChangesAsync();
        }

        private static async Task SeedPromotionsAsync(ApplicationDbContext db)
        {
            if (await db.Promotions.AnyAsync()) return;

            var year = PhilippineTime.Today.Year;
            var monthNames = new[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };

            for (var i = 0; i < monthNames.Length; i++)
            {
                var month = i + 1;
                var title = $"{monthNames[i]} Promo";
                db.Promotions.Add(new Promotion
                {
                    AnnouncementType = "Monthly Promo",
                    Title = title,
                    Description = $"Edit this {monthNames[i]} promotion with your court rates, event details, or facility announcement.",
                    ImagePath = $"/img/promos/months/{monthNames[i].ToLowerInvariant()}.svg",
                    ImageAlt = $"{title} artwork for The Royall Courts",
                    ButtonLabel = "Book Now",
                    ButtonUrl = "#booking-search",
                    IconHtml = "&#x1F389;",
                    StartDate = new DateTime(year, month, 1),
                    EndDate = new DateTime(year, month, DateTime.DaysInMonth(year, month)),
                    DisplayOrder = i,
                    IsActive = true,
                    UpdatedDate = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
        }
    }
}
