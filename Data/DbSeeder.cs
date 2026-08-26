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
            await ApplyGiuseppeFootballDefaultsAsync(db);
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
            var email = configuration["DefaultSuperAdmin:Email"] ?? "superadmin@giuseppefootball.com";
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
            var adminEmail = configuration["DefaultAdmin:Email"] ?? "admin@giuseppefootball.com";
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
            var email = configuration["DefaultStaff:Email"] ?? "staff@giuseppefootball.com";
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
                new()
                {
                    CourtName = "Giuseppe Football Field",
                    SportType = SportType.Football,
                    Description = "Football field available for regular matches, training, and private bookings.",
                    HourlyRate = 0m,
                    Status = CourtStatus.Available,
                    IsActive = true,
                    IsFullCourt = true
                }
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

        // Keeps one simple field group for the football setup. The internal
        // model still uses Court/CourtGroup names to avoid a database rename.
        private static async Task SeedCourtGroupsAsync(ApplicationDbContext db)
        {
            var footballGroup = await db.CourtGroups.FirstOrDefaultAsync(g => g.GroupCode == "FOOTBALL_FIELD");
            if (footballGroup == null)
            {
                footballGroup = new CourtGroup
                {
                    GroupCode = "FOOTBALL_FIELD",
                    GroupName = "Giuseppe Football Field",
                    Description = "Primary football field booking area.",
                    IsActive = true
                };
                db.CourtGroups.Add(footballGroup);
            }

            await db.SaveChangesAsync();

            var field = await db.Courts.FirstOrDefaultAsync(c => c.CourtName == "Giuseppe Football Field");
            if (field != null && field.CourtGroupId == null)
            {
                field.CourtGroupId = footballGroup.Id;
                field.IsFullCourt = true;
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
                WebsiteName = "Giuseppe Football",
                WebsiteTagline = "Football Field",
                NavbarLogoPath = null,
                LandingColorScheme = LandingColorSchemeCatalog.DefaultKey,
                HeroTitle = "Book Your Next Football Game",
                HeroSubtitle = "Reserve the Giuseppe Football field online in seconds for matches, training sessions, and friendly games.",
                HeroButtonText = "Find Available Field Times",
                UpdatedDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        private static async Task SeedAboutContentAsync(ApplicationDbContext db)
        {
            if (await db.AboutContents.AnyAsync()) return;

            db.AboutContents.Add(new AboutContent
            {
                Title = "About Giuseppe Football",
                Description = "Giuseppe Football is a dedicated football field built for casual games, training sessions, friendly matches, and competitive play. " +
                              "Players can reserve field time online, review available schedules, and arrive ready to play.",
                FacilitiesList = string.Join('\n', new[]
                {
                    "Well-kept football field",
                    "Clean changing rooms",
                    "Comfortable waiting area",
                    "Parking area",
                    "Equipment rental",
                    "Friendly staff",
                    "Online & walk-in booking"
                }),
                ContactPhone = "0917 622 0308",
                ContactEmail = "hello@giuseppefootball.ph",
                Location = "Giuseppe Football Club Sanchez Compound Banilad 6000 Cebu (PH)",
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
                    Title = "Giuseppe Football Field",
                    ImagePath = "https://images.unsplash.com/photo-1431324155629-1a6deb1dec8d?auto=format&fit=crop&w=1920&q=80",
                    IsDefault = true,
                    IsActive = true
                },
                new HeroImage
                {
                    Title = "Football Training Sessions",
                    ImagePath = "https://images.unsplash.com/photo-1517466787929-bc90951d0974?auto=format&fit=crop&w=1920&q=80",
                    IsDefault = false,
                    IsActive = true
                },
                new HeroImage
                {
                    Title = "Match Day Football",
                    ImagePath = "https://images.unsplash.com/photo-1522778119026-d647f0596c20?auto=format&fit=crop&w=1920&q=80",
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
                ("Football Field", "A well-kept field for casual games, league matches, and private bookings.", "https://images.unsplash.com/photo-1431324155629-1a6deb1dec8d?auto=format&fit=crop&w=640&q=80"),
                ("Training Session", "Open space for drills, team practice, and skill development.", "https://images.unsplash.com/photo-1517466787929-bc90951d0974?auto=format&fit=crop&w=640&q=80"),
                ("Match Day", "Reserve your schedule and arrive ready for kickoff.", "https://images.unsplash.com/photo-1522778119026-d647f0596c20?auto=format&fit=crop&w=640&q=80"),
                ("Team Play", "Flexible booking for friendly matches and competitive games.", "https://images.unsplash.com/photo-1579952363873-27f3bade9f55?auto=format&fit=crop&w=640&q=80")
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
                    Description = $"Edit this {monthNames[i]} promotion with your field rates, event details, or facility announcement.",
                    ImagePath = $"/img/promos/months/{monthNames[i].ToLowerInvariant()}.svg",
                    ImageAlt = $"{title} artwork for Giuseppe Football",
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

        private static async Task ApplyGiuseppeFootballDefaultsAsync(ApplicationDbContext db)
        {
            var now = DateTime.UtcNow;

            foreach (var settings in await db.WebsiteSettings.ToListAsync())
            {
                if (ContainsOldBrand(settings.WebsiteName)
                    || ContainsOldBrand(settings.HeroSubtitle)
                    || ContainsMisspelledBrand(settings.WebsiteName)
                    || ContainsMisspelledBrand(settings.HeroSubtitle)
                    || ContainsOldSport(settings.HeroSubtitle))
                {
                    settings.WebsiteName = "Giuseppe Football";
                    settings.WebsiteTagline = "Football Field";
                    settings.HeroTitle = "Book Your Next Football Game";
                    settings.HeroSubtitle = "Reserve the Giuseppe Football field online in seconds for matches, training sessions, and friendly games.";
                    settings.HeroButtonText = "Find Available Field Times";
                    settings.UpdatedDate = now;
                }
            }

            foreach (var about in await db.AboutContents.ToListAsync())
            {
                if (ContainsOldBrand(about.Title)
                    || ContainsOldBrand(about.Description)
                    || ContainsMisspelledBrand(about.Title)
                    || ContainsMisspelledBrand(about.Description)
                    || ContainsPlaceholderContact(about.ContactPhone)
                    || ContainsPlaceholderLocation(about.Location)
                    || ContainsOldSport(about.Description)
                    || ContainsOldSport(about.FacilitiesList))
                {
                    about.Title = "About Giuseppe Football";
                    about.Description = "Giuseppe Football is a dedicated football field built for casual games, training sessions, friendly matches, and competitive play. " +
                                        "Players can reserve field time online, review available schedules, and arrive ready to play.";
                    about.FacilitiesList = string.Join('\n', new[]
                    {
                        "Well-kept football field",
                        "Clean changing rooms",
                        "Comfortable waiting area",
                        "Parking area",
                        "Equipment rental",
                        "Friendly staff",
                        "Online & walk-in booking"
                    });
                    about.ContactPhone = "0917 622 0308";
                    about.ContactEmail = "hello@giuseppefootball.ph";
                    about.Location = "Giuseppe Football Club Sanchez Compound Banilad 6000 Cebu (PH)";
                    about.UpdatedDate = now;
                }
            }

            var oldSeedNames = new[]
            {
                "Basketball Court",
                "Pickleball Court 1",
                "Pickleball Court 2",
                "Pickleball Court 3",
                "Pickleball Court 4"
            };
            var existingCourts = await db.Courts.OrderBy(c => c.Id).ToListAsync();
            var footballField = existingCourts.FirstOrDefault(c => c.CourtName == "Giuseppe Football Field")
                ?? existingCourts.FirstOrDefault(c => ContainsMisspelledBrand(c.CourtName))
                ?? existingCourts.FirstOrDefault(c => oldSeedNames.Contains(c.CourtName));

            if (footballField == null)
            {
                footballField = new Court
                {
                    CourtName = "Giuseppe Football Field",
                    HourlyRate = 0m,
                    Status = CourtStatus.Available,
                    IsActive = true,
                    IsFullCourt = true
                };
                db.Courts.Add(footballField);
            }

            footballField.CourtName = "Giuseppe Football Field";
            footballField.SportType = SportType.Football;
            footballField.Description = "Football field available for regular matches, training, and private bookings.";
            footballField.Status = CourtStatus.Available;
            footballField.IsActive = true;
            footballField.IsFullCourt = true;

            foreach (var court in existingCourts.Where(c => c.Id != footballField.Id))
            {
                court.SportType = SportType.Football;
                if (ContainsMisspelledBrand(court.CourtName))
                {
                    court.CourtName = court.CourtName.Replace(MisspelledGiuseppe, "Giuseppe", StringComparison.OrdinalIgnoreCase);
                }

                if (oldSeedNames.Contains(court.CourtName))
                {
                    court.IsActive = false;
                }
            }

            var footballGroup = await db.CourtGroups.FirstOrDefaultAsync(g => g.GroupCode == "FOOTBALL_FIELD");
            if (footballGroup == null)
            {
                footballGroup = await db.CourtGroups.FirstOrDefaultAsync(g => g.GroupCode == "MAIN_COURT")
                    ?? new CourtGroup { GroupCode = "FOOTBALL_FIELD" };
                if (footballGroup.Id == 0)
                {
                    db.CourtGroups.Add(footballGroup);
                }
            }
            footballGroup.GroupCode = "FOOTBALL_FIELD";
            footballGroup.GroupName = "Giuseppe Football Field";
            footballGroup.Description = "Primary football field booking area.";
            footballGroup.IsActive = true;
            footballField.CourtGroup = footballGroup;

            foreach (var group in await db.CourtGroups.Where(g => g.GroupCode == "PB4").ToListAsync())
            {
                group.IsActive = false;
            }

            foreach (var hero in await db.HeroImages.ToListAsync())
            {
                if (hero.IsDefault && (ContainsOldBrand(hero.Title) || ContainsMisspelledBrand(hero.Title) || ContainsOldSport(hero.Title)))
                {
                    hero.Title = "Giuseppe Football Field";
                    hero.ImagePath = "https://images.unsplash.com/photo-1431324155629-1a6deb1dec8d?auto=format&fit=crop&w=1920&q=80";
                }
            }

            foreach (var promo in await db.Promotions.ToListAsync())
            {
                var needsUpdate = ContainsOldBrand(promo.ImageAlt)
                    || ContainsOldBrand(promo.Description)
                    || ContainsMisspelledBrand(promo.ImageAlt)
                    || ContainsMisspelledBrand(promo.Description)
                    || ContainsOldSport(promo.Description);
                if (promo.Description.Contains("court rates", StringComparison.OrdinalIgnoreCase))
                {
                    promo.Description = promo.Description.Replace("court rates", "field rates", StringComparison.OrdinalIgnoreCase);
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    promo.ImageAlt = $"{promo.Title} artwork for Giuseppe Football";
                    promo.UpdatedDate = now;
                }
            }

            await db.SaveChangesAsync();
        }

        private const string MisspelledGiuseppe = "Gui" + "ssepe";
        private const string PreviousRoyalBrand = "Royal " + "Court";
        private const string PreviousDoubleLBrand = "Roy" + "all";
        private const string PreviousSystemBrand = "Court" + "Book";
        private const string PreviousGcshPlaceholder = "0917-" + "XXX-XXXX";
        private const string PreviousSportsAvenue = "123 Sports " + "Avenue";
        private const string PreviousMakatiLocation = "Makati " + "City";

        private static bool ContainsOldBrand(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Contains(PreviousRoyalBrand, StringComparison.OrdinalIgnoreCase)
                || value.Contains(PreviousDoubleLBrand, StringComparison.OrdinalIgnoreCase)
                || value.Contains(PreviousSystemBrand, StringComparison.OrdinalIgnoreCase));

        private static bool ContainsMisspelledBrand(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value.Contains(MisspelledGiuseppe, StringComparison.OrdinalIgnoreCase);

        private static bool ContainsPlaceholderContact(string? value) =>
            string.IsNullOrWhiteSpace(value)
            || value.Contains(PreviousGcshPlaceholder, StringComparison.OrdinalIgnoreCase)
            || value.Contains("+63 917 123 4567", StringComparison.OrdinalIgnoreCase);

        private static bool ContainsPlaceholderLocation(string? value) =>
            string.IsNullOrWhiteSpace(value)
            || value.Contains(PreviousSportsAvenue, StringComparison.OrdinalIgnoreCase)
            || value.Contains(PreviousMakatiLocation, StringComparison.OrdinalIgnoreCase);

        private static bool ContainsOldSport(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Contains("Pickleball", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Badminton", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Basketball", StringComparison.OrdinalIgnoreCase)
                || value.Contains("Tennis", StringComparison.OrdinalIgnoreCase));
    }
}
