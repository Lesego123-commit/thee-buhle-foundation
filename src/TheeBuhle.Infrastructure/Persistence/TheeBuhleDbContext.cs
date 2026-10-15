// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;

namespace TheeBuhle.Infrastructure.Persistence;

/// <summary>
/// The single database context.
/// </summary>
/// <remarks>
/// <para>
/// One context for the whole application rather than one per module. The
/// boundaries in this system are enforced by the repository interfaces in the
/// Domain layer, and splitting the context would mean losing the ability to save
/// a register entry and the badge it earned in one transaction — which is
/// exactly the guarantee <c>AwardBadgesOnAttendance</c> depends on.
/// </para>
/// <para>
/// <strong>Moving from MySQL to SQL Server made two rules better, not just
/// different.</strong> The PHP build ran on MySQL, which has no filtered unique
/// index, so "at most one safeguarding lead" and "one active mentor per learner
/// per year" had to be faked with stored generated columns that are NULL for
/// non-matching rows — relying on the fact that NULLs do not collide in a unique
/// key. That worked, but it put two columns in the schema that exist only to
/// work around a missing feature, and anybody reading the table had to decode
/// the trick.
/// </para>
/// <para>
/// SQL Server supports <c>CREATE UNIQUE INDEX ... WHERE</c>, so both rules are
/// now written as what they actually are, and the two workaround columns are
/// gone. See <see cref="ConfigurePeople"/> and <see cref="ConfigureDelivery"/>.
/// </para>
/// </remarks>
public sealed class TheeBuhleDbContext(DbContextOptions<TheeBuhleDbContext> options, IClock clock)
    : DbContext(options)
{
    // --------------------------------------------------------------- people
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AccountCode> AccountCodes => Set<AccountCode>();

    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    public DbSet<Learner> Learners => Set<Learner>();

    public DbSet<ParentGuardian> ParentGuardians => Set<ParentGuardian>();

    public DbSet<GuardianLink> GuardianLinks => Set<GuardianLink>();

    public DbSet<Mentor> Mentors => Set<Mentor>();

    public DbSet<StaffMember> Staff => Set<StaffMember>();

    // ------------------------------------------------------------- delivery
    public DbSet<Programme> Programmes => Set<Programme>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Enrolment> Enrolments => Set<Enrolment>();

    public DbSet<TutoringSession> Sessions => Set<TutoringSession>();

    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();

    public DbSet<MentorAssignment> MentorAssignments => Set<MentorAssignment>();

    public DbSet<CheckIn> CheckIns => Set<CheckIn>();

    // ------------------------------------------------------------- learning
    public DbSet<Subject> Subjects => Set<Subject>();

    public DbSet<LearningResource> Resources => Set<LearningResource>();

    public DbSet<ResourceProgress> ResourceProgress => Set<ResourceProgress>();

    public DbSet<AssessmentResult> AssessmentResults => Set<AssessmentResult>();

    public DbSet<Badge> Badges => Set<Badge>();

    public DbSet<LearnerBadge> LearnerBadges => Set<LearnerBadge>();

    public DbSet<PeerGroup> PeerGroups => Set<PeerGroup>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<BuddyRequest> BuddyRequests => Set<BuddyRequest>();

    public DbSet<LearnerSummary> LearnerSummaries => Set<LearnerSummary>();

    // ----------------------------------------------------------- engagement
    public DbSet<Donor> Donors => Set<Donor>();

    public DbSet<Donation> Donations => Set<Donation>();

    public DbSet<Sponsorship> Sponsorships => Set<Sponsorship>();

    public DbSet<PublicEvent> Events => Set<PublicEvent>();

    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();

    public DbSet<VolunteerApplication> VolunteerApplications => Set<VolunteerApplication>();

    public DbSet<ComplianceDocument> ComplianceDocuments => Set<ComplianceDocument>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<MessageRecipient> MessageRecipients => Set<MessageRecipient>();

    public DbSet<PermissionForm> PermissionForms => Set<PermissionForm>();

    public DbSet<Consent> Consents => Set<Consent>();

    // ---------------------------------------------------------- crosscutting
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NewsPost> NewsPosts => Set<NewsPost>();

    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    public DbSet<NewsletterSubscriber> NewsletterSubscribers => Set<NewsletterSubscriber>();

    public DbSet<ImpactMetric> ImpactMetrics => Set<ImpactMetric>();

    /// <summary>
    /// Stamps timestamps, then saves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Done here rather than with database defaults, and that is deliberate. A
    /// <c>DEFAULT CURRENT_TIMESTAMP</c> uses the <em>database server's</em> clock
    /// and its timezone, which on Azure SQL is UTC and on a developer's machine
    /// is whatever they have set. Stamping through <see cref="IClock"/> means
    /// every timestamp in the system comes from one pinned source
    /// (Africa/Johannesburg), including in tests, where the clock is frozen.
    /// </para>
    /// <para>
    /// It also means a fake clock can move time forward in a test and every
    /// entity written afterwards agrees — which is how the token-expiry and
    /// clearance-expiry tests work at all.
    /// </para>
    /// </remarks>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampTimestamps();

        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigurePeople(modelBuilder);
        ConfigureDelivery(modelBuilder);
        ConfigureLearning(modelBuilder);
        ConfigureEngagement(modelBuilder);
        ConfigureCrossCutting(modelBuilder);
        ConfigureActorReferences(modelBuilder);

        // Both run last, over the finished model, so they catch every property
        // including the ones the configuration methods above added.
        ModelConventions.StoreEnumsAsText(modelBuilder);
        ModelConventions.UseSnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// The "who did this" columns, constrained to real users.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Thirteen columns record an actor — who captured a register, who closed a
    /// safeguarding concern, who a concern was escalated to — and until this
    /// was added none of them was a foreign key. The database would accept any
    /// identifier at all, and it did: the development seeder wrote an invented
    /// one into every attendance mark and every school result, so seventy
    /// register entries were "recorded by" a user who never existed. The public
    /// Impact page states that every register entry records who captured it.
    /// Without the constraint, that sentence was a hope.
    /// </para>
    /// <para>
    /// <see cref="DeleteBehavior.NoAction"/> throughout. A user who has
    /// captured a register or closed a concern cannot be deleted out from under
    /// that record; the account is deactivated instead. A cascade here would
    /// delete safeguarding history along with the person who wrote it, and a
    /// set-null would erase the author of a decision that has to have one.
    /// </para>
    /// <para>
    /// Navigation-free on purpose: the domain entities keep their plain
    /// <see cref="Guid"/> properties, so nothing that reads a check-in starts
    /// loading user rows it did not ask for.
    /// </para>
    /// </remarks>
    private static void ConfigureActorReferences(ModelBuilder model)
    {
        model.Entity<Announcement>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.PublishedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<AssessmentResult>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.CapturedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<AttendanceRecord>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.RecordedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<AuditLogEntry>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);

        model.Entity<BuddyRequest>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.ApprovedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<CheckIn>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.ClosedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<CheckIn>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.EscalatedTo).OnDelete(DeleteBehavior.NoAction);

        model.Entity<ComplianceDocument>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.VerifiedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<ContactMessage>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.HandledBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<MentorAssignment>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.ApprovedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<PeerGroup>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.ApprovedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<PermissionForm>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.CreatedBy).OnDelete(DeleteBehavior.NoAction);

        model.Entity<VolunteerApplication>().HasOne<User>().WithMany()
            .HasForeignKey(e => e.ReviewedBy).OnDelete(DeleteBehavior.NoAction);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money. 18,2 rather than the EF default of 18,2-with-a-warning: an
        // amount in rands and cents needs exactly two decimal places, and a
        // float would make R1 800.00 occasionally store as R1 799.99999.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        base.ConfigureConventions(configurationBuilder);
    }

    private void StampTimestamps()
    {
        var now = clock.Now;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    // CreatedAt is explicitly marked unchanged rather than
                    // simply left alone, because an entity round-tripped
                    // through an API and re-attached would otherwise overwrite
                    // it with whatever the client sent — including default.
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Entity.UpdatedAt = now;
                    break;

                default:
                    break;
            }
        }
    }

    private static void ConfigurePeople(ModelBuilder model)
    {
        model.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(190).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(255).IsRequired();

            // Case-insensitive so "Thabo@example.com" and "thabo@example.com"
            // are one account. Enforced by the collation rather than by
            // lower-casing in code, because code that lower-cases on write and
            // forgets on read creates a duplicate nobody can sign in to.
            entity.Property(user => user.Email).UseCollation("Latin1_General_CI_AS");
            entity.HasIndex(user => user.Email).IsUnique().HasDatabaseName("uk_users_email");
            entity.HasIndex(user => new { user.Role, user.Status }).HasDatabaseName("ix_users_role");
        });

        model.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(token => token.Id);

            // The hash, never the token. A stolen database backup must not be a
            // set of working sessions.
            entity.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("uk_refresh_hash");
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt }).HasDatabaseName("ix_refresh_user");

            entity.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AccountCode>(entity =>
        {
            entity.ToTable("account_codes", table =>
            {
                table.HasCheckConstraint("ck_account_codes_attempts", "[failed_attempts] >= 0");
                table.HasCheckConstraint("ck_account_codes_expiry", "[expires_at] > [created_at]");
            });

            entity.HasKey(code => code.Id);

            // The hash, never the code, for the same reason as refresh tokens.
            entity.Property(code => code.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(code => code.Purpose).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(code => new { code.UserId, code.UsedAt, code.RevokedAt })
                .HasDatabaseName("ix_account_codes_user");

            // Deleting an account takes its codes with it: a code for an
            // account that no longer exists opens nothing.
            entity.HasOne(code => code.User)
                .WithMany()
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Who issued it stays attributable, and an issuer cannot be deleted
            // out from under the record.
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(code => code.IssuedBy)
                .OnDelete(DeleteBehavior.NoAction);
        });

        model.Entity<DeviceToken>(entity =>
        {
            entity.ToTable("device_tokens");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Token).HasMaxLength(255).IsRequired();
            entity.HasIndex(token => token.Token).IsUnique().HasDatabaseName("uk_device_token");

            entity.HasOne(token => token.User)
                .WithMany(user => user.DeviceTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Learner>(entity =>
        {
            entity.ToTable("learners", table => table.HasCheckConstraint(
                "ck_learners_grade",
                "[grade] BETWEEN 1 AND 12"));

            entity.HasKey(learner => learner.Id);
            entity.Property(learner => learner.ReferenceCode).HasMaxLength(12).IsRequired();
            entity.Property(learner => learner.FirstName).HasMaxLength(80).IsRequired();
            entity.Property(learner => learner.Surname).HasMaxLength(80).IsRequired();
            entity.Property(learner => learner.School).HasMaxLength(140);
            entity.Property(learner => learner.Suburb).HasMaxLength(80);
            entity.Property(learner => learner.Ambition).HasMaxLength(255);

            entity.HasIndex(learner => learner.ReferenceCode)
                .IsUnique()
                .HasDatabaseName("uk_learners_reference");

            entity.HasIndex(learner => learner.Grade).HasDatabaseName("ix_learners_grade");

            // A learner account is optional — a Grade 4 learner has no login —
            // so the unique index is filtered. Without the filter every
            // learner without a user id would collide with every other one.
            entity.HasIndex(learner => learner.UserId)
                .IsUnique()
                .HasFilter("[user_id] IS NOT NULL")
                .HasDatabaseName("uk_learners_user");

            entity.HasOne(learner => learner.User)
                .WithOne(user => user.LearnerProfile)
                .HasForeignKey<Learner>(learner => learner.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ParentGuardian>(entity =>
        {
            entity.ToTable("parent_guardians");
            entity.HasKey(parent => parent.Id);
            entity.Property(parent => parent.FirstName).HasMaxLength(80).IsRequired();
            entity.Property(parent => parent.Surname).HasMaxLength(80).IsRequired();
            entity.Property(parent => parent.Relationship).HasMaxLength(40);
            entity.Property(parent => parent.Phone).HasMaxLength(30).IsRequired();
            entity.Property(parent => parent.AltPhone).HasMaxLength(30);

            entity.HasIndex(parent => parent.UserId).IsUnique().HasDatabaseName("uk_parent_user");

            entity.HasOne(parent => parent.User)
                .WithOne(user => user.ParentProfile)
                .HasForeignKey<ParentGuardian>(parent => parent.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<GuardianLink>(entity =>
        {
            entity.ToTable("guardian_links");

            // A composite key, not a surrogate. The pair *is* the identity of
            // the relationship, and a surrogate key would allow the same parent
            // to be linked to the same learner twice.
            entity.HasKey(link => new { link.ParentId, link.LearnerId });

            entity.HasOne(link => link.Parent)
                .WithMany(parent => parent.GuardianLinks)
                .HasForeignKey(link => link.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(link => link.Learner)
                .WithMany(learner => learner.GuardianLinks)
                .HasForeignKey(link => link.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // The index that every parent-facing authorisation check hits.
            entity.HasIndex(link => link.LearnerId).HasDatabaseName("ix_guardian_learner");
        });

        model.Entity<Mentor>(entity =>
        {
            entity.ToTable("mentors", table => table.HasCheckConstraint(
                "ck_mentor_clearance",
                // Half the rule. A cleared mentor must have an expiry date —
                // which the database can check — but whether that date is in
                // the past it cannot, because a CHECK constraint may not
                // reference the current date. The other half lives in
                // MentorshipService.AssertCleared, and the assignment path is
                // the only way to create an assignment.
                "[clearance_status] <> 'Cleared' OR [clearance_expiry] IS NOT NULL"));

            entity.HasKey(mentor => mentor.Id);
            entity.Property(mentor => mentor.FirstName).HasMaxLength(80).IsRequired();
            entity.Property(mentor => mentor.Surname).HasMaxLength(80).IsRequired();
            entity.Property(mentor => mentor.Occupation).HasMaxLength(120);

            entity.HasIndex(mentor => mentor.UserId).IsUnique().HasDatabaseName("uk_mentor_user");

            entity.HasIndex(mentor => new { mentor.ClearanceStatus, mentor.ClearanceExpiry })
                .HasDatabaseName("ix_mentor_clearance");

            entity.HasOne(mentor => mentor.User)
                .WithOne(user => user.MentorProfile)
                .HasForeignKey<Mentor>(mentor => mentor.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<StaffMember>(entity =>
        {
            entity.ToTable("staff");
            entity.HasKey(staff => staff.Id);
            entity.Property(staff => staff.FirstName).HasMaxLength(80).IsRequired();
            entity.Property(staff => staff.Surname).HasMaxLength(80).IsRequired();
            entity.Property(staff => staff.JobTitle).HasMaxLength(120).IsRequired();

            entity.HasIndex(staff => staff.UserId).IsUnique().HasDatabaseName("uk_staff_user");

            // ================================================================
            //  "At most one safeguarding lead", as a real filtered index.
            //
            //  On MySQL this rule needed a stored generated column that was
            //  NULL for every non-lead row, because NULLs do not collide in a
            //  unique key — a working trick, but one that put a column in the
            //  schema whose only purpose was to work around a missing feature.
            //
            //  SQL Server indexes the predicate directly, so the rule reads as
            //  what it is and the workaround column is gone. The rule itself
            //  matters: the designated safeguarding lead is who flagged
            //  concerns route to, and two of them means a concern reaches an
            //  arbitrary one of the two.
            // ================================================================
            entity.HasIndex(staff => staff.IsSafeguardingLead)
                .IsUnique()
                .HasFilter("[is_safeguarding_lead] = 1")
                .HasDatabaseName("uk_staff_single_lead");

            entity.HasOne(staff => staff.User)
                .WithOne(user => user.StaffProfile)
                .HasForeignKey<StaffMember>(staff => staff.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDelivery(ModelBuilder model)
    {
        model.Entity<Programme>(entity =>
        {
            entity.ToTable("programmes", table => table.HasCheckConstraint(
                "ck_programme_grades",
                "[max_grade] >= [min_grade]"));

            entity.HasKey(programme => programme.Id);
            entity.Property(programme => programme.Slug).HasMaxLength(60).IsRequired();
            entity.Property(programme => programme.Number).HasMaxLength(4).IsRequired();
            entity.Property(programme => programme.Name).HasMaxLength(140).IsRequired();
            entity.Property(programme => programme.Summary).HasMaxLength(400).IsRequired();
            entity.Property(programme => programme.HowToJoin).HasMaxLength(400);
            entity.Property(programme => programme.CtaLabel).HasMaxLength(60);
            entity.Property(programme => programme.Audience).HasMaxLength(140);
            entity.Property(programme => programme.CapacityNote).HasMaxLength(140);

            // A list of strings that is never queried relationally (Task 1
            // section 9.3.11.4), so it is one JSON column rather than a table
            // and a join for something only ever rendered as a bullet list.
            // One JSON column rather than a table and a join, because the list
            // is only ever rendered as bullet points and never queried.
            entity.Property(programme => programme.Expectations)
                .HasConversion(ModelConventions.StringListConverter)
                .HasColumnName("expectations")
                .HasMaxLength(2000)
                .Metadata.SetValueComparer(ModelConventions.StringListComparer);

            entity.HasIndex(programme => programme.Slug).IsUnique().HasDatabaseName("uk_programme_slug");
        });

        model.Entity<Venue>(entity =>
        {
            entity.ToTable("venues");
            entity.HasKey(venue => venue.Id);
            entity.Property(venue => venue.Name).HasMaxLength(140).IsRequired();
            entity.Property(venue => venue.Address).HasMaxLength(255).IsRequired();
        });

        model.Entity<Enrolment>(entity =>
        {
            entity.ToTable("enrolments");
            entity.HasKey(enrolment => enrolment.Id);
            entity.Property(enrolment => enrolment.Term).HasMaxLength(8).IsRequired();

            // One enrolment per learner per programme per term. A learner
            // enrolled twice counts twice in every attendance denominator.
            entity.HasIndex(enrolment => new { enrolment.LearnerId, enrolment.ProgrammeId, enrolment.Term })
                .IsUnique()
                .HasDatabaseName("uk_enrolment_unique");

            entity.HasOne(enrolment => enrolment.Learner)
                .WithMany(learner => learner.Enrolments)
                .HasForeignKey(enrolment => enrolment.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(enrolment => enrolment.Programme)
                .WithMany(programme => programme.Enrolments)
                .HasForeignKey(enrolment => enrolment.ProgrammeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<TutoringSession>(entity =>
        {
            entity.ToTable("sessions", table => table.HasCheckConstraint(
                "ck_session_window",
                "[ends_at] > [starts_at]"));

            entity.HasKey(session => session.Id);
            entity.Property(session => session.SubjectFocus).HasMaxLength(120);
            entity.Property(session => session.Room).HasMaxLength(40);

            entity.HasIndex(session => session.StartsAt).HasDatabaseName("ix_session_starts");

            entity.HasOne(session => session.Programme)
                .WithMany(programme => programme.Sessions)
                .HasForeignKey(session => session.ProgrammeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(session => session.Venue)
                .WithMany(venue => venue.Sessions)
                .HasForeignKey(session => session.VenueId)
                // Restrict, not cascade: deleting a venue must not silently
                // delete the attendance history of every session held in it.
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<AttendanceRecord>(entity =>
        {
            entity.ToTable("attendance");
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Note).HasMaxLength(255);

            // One mark per learner per session. This is what makes capturing a
            // register twice an error rather than a double count — and the
            // service catches the violation and reports it as a sentence.
            entity.HasIndex(record => new { record.SessionId, record.LearnerId })
                .IsUnique()
                .HasDatabaseName("uk_attendance_once");

            entity.HasIndex(record => new { record.LearnerId, record.Status })
                .HasDatabaseName("ix_attendance_learner");

            entity.HasOne(record => record.Session)
                .WithMany(session => session.Attendance)
                .HasForeignKey(record => record.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(record => record.Learner)
                .WithMany(learner => learner.Attendance)
                .HasForeignKey(record => record.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MentorAssignment>(entity =>
        {
            entity.ToTable("mentor_assignments");
            entity.HasKey(assignment => assignment.Id);

            // ================================================================
            //  "One active mentor per learner per school year."
            //
            //  The second rule MySQL had to fake with a generated column. Here
            //  it is the predicate itself: unique on (learner, year) but only
            //  over active rows, so a learner may have last year's ended
            //  assignment and this year's active one, and may not have two
            //  active ones.
            //
            //  Handing a child between mentors mid-year is a decision somebody
            //  has to make explicitly, which is why ending the old assignment
            //  is a required step rather than something the system does
            //  quietly.
            // ================================================================
            entity.HasIndex(assignment => new { assignment.LearnerId, assignment.SchoolYear })
                .IsUnique()
                .HasFilter("[status] = 'Active'")
                .HasDatabaseName("uk_assignment_active");

            entity.HasIndex(assignment => new { assignment.MentorId, assignment.Status })
                .HasDatabaseName("ix_assignment_mentor");

            entity.HasOne(assignment => assignment.Mentor)
                .WithMany(mentor => mentor.Assignments)
                .HasForeignKey(assignment => assignment.MentorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(assignment => assignment.Learner)
                .WithMany(learner => learner.MentorAssignments)
                .HasForeignKey(assignment => assignment.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<CheckIn>(entity =>
        {
            entity.ToTable("check_ins");
            entity.HasKey(checkIn => checkIn.Id);
            entity.Property(checkIn => checkIn.Notes).HasMaxLength(4000);
            entity.Property(checkIn => checkIn.Outcome).HasMaxLength(2000);

            entity.Property(checkIn => checkIn.SupportAreas)
                .HasConversion(ModelConventions.StringListConverter)
                .HasMaxLength(500)
                .Metadata.SetValueComparer(ModelConventions.StringListComparer);

            entity.HasIndex(checkIn => new { checkIn.AssignmentId, checkIn.OccurredOn })
                .HasDatabaseName("ix_checkin_assignment");

            // The index the safeguarding queue reads. Covering the flag and the
            // escalation time means "open flagged concerns" is an index seek
            // rather than a scan over every note ever written.
            // The safeguarding queue's index. Filtered on "flagged and still
            // open", so the queue is an index seek over a handful of rows
            // rather than a scan across every note ever written — and it stays
            // that way as the check-in table grows.
            entity.HasIndex(checkIn => checkIn.OccurredOn)
                .HasFilter("[concern_flagged] = 1 AND [closed_at] IS NULL")
                .HasDatabaseName("ix_checkin_concern_open");

            entity.HasOne(checkIn => checkIn.Assignment)
                .WithMany(assignment => assignment.CheckIns)
                .HasForeignKey(checkIn => checkIn.AssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureLearning(ModelBuilder model)
    {
        model.Entity<Subject>(entity =>
        {
            entity.ToTable("subjects");
            entity.HasKey(subject => subject.Id);
            entity.Property(subject => subject.Name).HasMaxLength(80).IsRequired();
            entity.Property(subject => subject.Icon).HasMaxLength(8);
            entity.Property(subject => subject.Accent).HasMaxLength(20);

            entity.HasIndex(subject => new { subject.Name, subject.Grade })
                .IsUnique()
                .HasDatabaseName("uk_subject_name_grade");
        });

        model.Entity<LearningResource>(entity =>
        {
            entity.ToTable("resources");
            entity.HasKey(resource => resource.Id);
            entity.Property(resource => resource.Title).HasMaxLength(200).IsRequired();
            entity.Property(resource => resource.Topic).HasMaxLength(140);
            entity.Property(resource => resource.StoragePath).HasMaxLength(500).IsRequired();

            entity.HasIndex(resource => new { resource.SubjectId, resource.Grade })
                .HasDatabaseName("ix_resource_subject");

            entity.HasOne(resource => resource.Subject)
                .WithMany(subject => subject.Resources)
                .HasForeignKey(resource => resource.SubjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ResourceProgress>(entity =>
        {
            entity.ToTable("resource_progress", table => table.HasCheckConstraint(
                "ck_rp_percent",
                "[percent_complete] BETWEEN 0 AND 100"));

            entity.HasKey(progress => new { progress.LearnerId, progress.ResourceId });

            entity.HasOne(progress => progress.Learner)
                .WithMany(learner => learner.ResourceProgress)
                .HasForeignKey(progress => progress.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(progress => progress.Resource)
                .WithMany(resource => resource.Progress)
                .HasForeignKey(progress => progress.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AssessmentResult>(entity =>
        {
            entity.ToTable("assessment_results", table => table.HasCheckConstraint(
                "ck_ar_mark",
                "[mark] BETWEEN 0 AND 100"));

            entity.HasKey(result => result.Id);
            entity.Property(result => result.Term).HasMaxLength(8).IsRequired();
            entity.Property(result => result.Mark).HasPrecision(5, 2);

            // Unique on the source as well as the term, so a school report mark
            // and an internal assessment for the same term coexist — and a
            // second school report for the same term replaces rather than
            // duplicates. The improvement figure is computed from school
            // reports only, and duplicates would skew it.
            entity.HasIndex(result => new { result.LearnerId, result.SubjectId, result.Term, result.Source })
                .IsUnique()
                .HasDatabaseName("uk_result_once");

            entity.HasOne(result => result.Learner)
                .WithMany(learner => learner.Results)
                .HasForeignKey(result => result.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(result => result.Subject)
                .WithMany(subject => subject.Results)
                .HasForeignKey(result => result.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Badge>(entity =>
        {
            entity.ToTable("badges");
            entity.HasKey(badge => badge.Id);
            entity.Property(badge => badge.Code).HasMaxLength(40).IsRequired();
            entity.Property(badge => badge.Name).HasMaxLength(80).IsRequired();
            entity.Property(badge => badge.Description).HasMaxLength(255).IsRequired();
            entity.Property(badge => badge.Icon).HasMaxLength(8);

            entity.HasIndex(badge => badge.Code).IsUnique().HasDatabaseName("uk_badge_code");
        });

        model.Entity<LearnerBadge>(entity =>
        {
            entity.ToTable("learner_badges");

            // Composite key, which is also the "award once" rule: a learner
            // cannot hold the same badge twice, so the badge evaluator can be
            // re-run on every attendance mark without accumulating duplicates.
            entity.HasKey(award => new { award.LearnerId, award.BadgeId });

            entity.HasOne(award => award.Learner)
                .WithMany(learner => learner.Badges)
                .HasForeignKey(award => award.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(award => award.Badge)
                .WithMany(badge => badge.AwardedTo)
                .HasForeignKey(award => award.BadgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<PeerGroup>(entity =>
        {
            entity.ToTable("peer_groups");
            entity.HasKey(group => group.Id);
            entity.Property(group => group.Name).HasMaxLength(140).IsRequired();
            entity.Property(group => group.MeetsAt).HasMaxLength(120);
            entity.Property(group => group.Location).HasMaxLength(140);

            entity.HasOne(group => group.Subject)
                .WithMany()
                .HasForeignKey(group => group.SubjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(group => group.Venue)
                .WithMany()
                .HasForeignKey(group => group.VenueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<GroupMembership>(entity =>
        {
            entity.ToTable("group_memberships");
            entity.HasKey(membership => new { membership.GroupId, membership.LearnerId });

            entity.HasOne(membership => membership.Group)
                .WithMany(group => group.Memberships)
                .HasForeignKey(membership => membership.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(membership => membership.Learner)
                .WithMany()
                .HasForeignKey(membership => membership.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<BuddyRequest>(entity =>
        {
            entity.ToTable("buddy_requests", table => table.HasCheckConstraint(
                "ck_buddy_not_self",
                "[requester_id] <> [recipient_id]"));

            entity.HasKey(request => request.Id);

            entity.HasIndex(request => new { request.RequesterId, request.RecipientId })
                .IsUnique()
                .HasDatabaseName("uk_buddy_pair");

            entity.HasIndex(request => request.Status).HasDatabaseName("ix_buddy_status");

            entity.HasOne(request => request.Requester)
                .WithMany()
                .HasForeignKey(request => request.RequesterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(request => request.Recipient)
                .WithMany()
                .HasForeignKey(request => request.RecipientId)
                // No cascade on the second leg: SQL Server refuses multiple
                // cascade paths to one table, and it would be wrong anyway —
                // deleting one learner should not delete requests belonging to
                // another.
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<LearnerSummary>(entity =>
        {
            entity.ToTable("learner_summaries");
            entity.HasKey(summary => summary.LearnerId);

            entity.HasOne(summary => summary.Learner)
                .WithOne()
                .HasForeignKey<LearnerSummary>(summary => summary.LearnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureEngagement(ModelBuilder model)
    {
        model.Entity<Donor>(entity =>
        {
            entity.ToTable("donors");
            entity.HasKey(donor => donor.Id);
            entity.Property(donor => donor.DisplayName).HasMaxLength(140).IsRequired();
            entity.Property(donor => donor.Email).HasMaxLength(190).IsRequired();
            entity.Property(donor => donor.Phone).HasMaxLength(30);

            // Deduplicated on email, so a monthly giver does not accumulate one
            // donor row a month — which would break the Section 18A annual
            // total, the figure a donor puts on their tax return.
            entity.Property(donor => donor.Email).UseCollation("Latin1_General_CI_AS");
            entity.HasIndex(donor => donor.Email).IsUnique().HasDatabaseName("uk_donor_email");
        });

        model.Entity<Donation>(entity =>
        {
            entity.ToTable("donations", table => table.HasCheckConstraint(
                "ck_donation_amount",
                "[amount] > 0"));

            entity.HasKey(donation => donation.Id);
            entity.Property(donation => donation.Currency).HasMaxLength(3).IsRequired();
            entity.Property(donation => donation.Designation).HasMaxLength(120);
            entity.Property(donation => donation.Gateway).HasMaxLength(40).IsRequired();
            entity.Property(donation => donation.GatewayReference).HasMaxLength(60).IsRequired();

            // The unique reference is the idempotency key. It is what makes a
            // webhook delivered three times — which gateways do — complete the
            // donation once instead of counting it three times.
            entity.HasIndex(donation => donation.GatewayReference)
                .IsUnique()
                .HasDatabaseName("uk_donation_gateway_ref");

            entity.HasIndex(donation => new { donation.Status, donation.ReceivedAt })
                .HasDatabaseName("ix_donation_status");

            // Finds the donations whose certificate never sent, for the nightly
            // retry. Filtered, so the index holds only the handful of rows the
            // job actually looks at rather than every donation ever made.
            entity.HasIndex(donation => donation.Section18AIssuedAt)
                .HasFilter("[section18_a_issued_at] IS NULL AND [wants_section18_a] = 1")
                .HasDatabaseName("ix_donation_s18a_pending");

            entity.HasOne(donation => donation.Donor)
                .WithMany(donor => donor.Donations)
                .HasForeignKey(donation => donation.DonorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Sponsorship>(entity =>
        {
            entity.ToTable("sponsorships", table => table.HasCheckConstraint(
                "ck_sp_window",
                "[end_date] IS NULL OR [end_date] > [start_date]"));

            entity.HasKey(sponsorship => sponsorship.Id);

            // One active sponsor per learner, as a filtered index. Two sponsors
            // told they are funding the same child is a conversation nobody
            // wants to have.
            entity.HasIndex(sponsorship => sponsorship.LearnerId)
                .IsUnique()
                .HasFilter("[end_date] IS NULL")
                .HasDatabaseName("uk_sponsorship_active");

            entity.HasOne(sponsorship => sponsorship.Donor)
                .WithMany(donor => donor.Sponsorships)
                .HasForeignKey(sponsorship => sponsorship.DonorId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(sponsorship => sponsorship.Learner)
                .WithMany()
                .HasForeignKey(sponsorship => sponsorship.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PublicEvent>(entity =>
        {
            entity.ToTable("events", table => table.HasCheckConstraint(
                "ck_event_window",
                "[ends_at] IS NULL OR [ends_at] >= [starts_at]"));

            entity.HasKey(publicEvent => publicEvent.Id);
            entity.Property(publicEvent => publicEvent.Title).HasMaxLength(200).IsRequired();
            entity.Property(publicEvent => publicEvent.Slug).HasMaxLength(80).IsRequired();
            entity.Property(publicEvent => publicEvent.Location).HasMaxLength(200);
            entity.Property(publicEvent => publicEvent.FeeNote).HasMaxLength(200);
            entity.Property(publicEvent => publicEvent.Description).HasMaxLength(4000);

            entity.HasIndex(publicEvent => publicEvent.Slug).IsUnique().HasDatabaseName("uk_event_slug");

            entity.HasIndex(publicEvent => new { publicEvent.IsPublished, publicEvent.StartsAt })
                .HasDatabaseName("ix_event_published");

            entity.HasOne(publicEvent => publicEvent.Venue)
                .WithMany()
                .HasForeignKey(publicEvent => publicEvent.VenueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<EventRegistration>(entity =>
        {
            entity.ToTable("event_registrations", table => table.HasCheckConstraint(
                "ck_er_party",
                "[party_size] BETWEEN 1 AND 20"));

            entity.HasKey(registration => registration.Id);
            entity.Property(registration => registration.AttendeeName).HasMaxLength(140).IsRequired();
            entity.Property(registration => registration.Email).HasMaxLength(190).IsRequired();
            entity.Property(registration => registration.Phone).HasMaxLength(30);

            // One registration per address per event. Backs up the service's
            // check, so two browser tabs racing produce an error rather than a
            // second booking that double-counts against capacity.
            entity.Property(registration => registration.Email).UseCollation("Latin1_General_CI_AS");
            entity.HasIndex(registration => new { registration.EventId, registration.Email })
                .IsUnique()
                .HasDatabaseName("uk_event_reg_email");

            entity.HasOne(registration => registration.Event)
                .WithMany(publicEvent => publicEvent.Registrations)
                .HasForeignKey(registration => registration.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(registration => registration.Learner)
                .WithMany()
                .HasForeignKey(registration => registration.LearnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<VolunteerApplication>(entity =>
        {
            entity.ToTable("volunteer_applications");
            entity.HasKey(application => application.Id);
            entity.Property(application => application.FirstName).HasMaxLength(80).IsRequired();
            entity.Property(application => application.Surname).HasMaxLength(80).IsRequired();
            entity.Property(application => application.Email).HasMaxLength(190).IsRequired();
            entity.Property(application => application.Phone).HasMaxLength(30).IsRequired();
            entity.Property(application => application.Suburb).HasMaxLength(80);
            entity.Property(application => application.Experience).HasMaxLength(4000);

            // Four digits, and the column is four characters wide. The width is
            // part of the POPIA data-minimisation commitment (Task 1 section
            // 9.3.13.6) rather than a formatting detail: a full South African ID
            // number cannot be stored here even by a bug.
            entity.Property(application => application.IdNumberLast4).HasMaxLength(4).IsFixedLength();

            entity.Property(application => application.Skills)
                .HasConversion(ModelConventions.StringListConverter)
                .HasMaxLength(500)
                .Metadata.SetValueComparer(ModelConventions.StringListComparer);

            // "Saturday mornings, Wednesday evenings" — a shape that is read
            // whole by one staff member and never aggregated over.
            entity.Property(application => application.Availability)
                .HasConversion(ModelConventions.AvailabilityConverter)
                .HasColumnName("availability")
                .HasMaxLength(2000)
                .Metadata.SetValueComparer(ModelConventions.AvailabilityComparer);

            entity.HasIndex(application => new { application.Status, application.SubmittedAt })
                .HasDatabaseName("ix_volunteer_status");

            entity.HasOne(application => application.User)
                .WithMany()
                .HasForeignKey(application => application.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ComplianceDocument>(entity =>
        {
            entity.ToTable("compliance_documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.StoragePath).HasMaxLength(500).IsRequired();

            entity.HasOne(document => document.Application)
                .WithMany(application => application.Documents)
                .HasForeignKey(document => document.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Announcement>(entity =>
        {
            entity.ToTable("announcements");
            entity.HasKey(announcement => announcement.Id);
            entity.Property(announcement => announcement.Title).HasMaxLength(200).IsRequired();

            // The only markup the system stores, and therefore the only place
            // stored XSS is possible. It is sanitised on write against an
            // allow-list, so the column cannot contain a payload at all.
            entity.Property(announcement => announcement.BodyHtml).IsRequired();

            entity.HasIndex(announcement => new { announcement.Audience, announcement.PublishedAt })
                .HasDatabaseName("ix_announcement_audience");
        });

        model.Entity<MessageRecipient>(entity =>
        {
            entity.ToTable("message_recipients");

            // The compliance record of who a notice went to. Composite key, so
            // one recipient appears once per announcement and "we told you" is
            // a single unambiguous row.
            entity.HasKey(recipient => new { recipient.AnnouncementId, recipient.UserId });

            entity.HasIndex(recipient => new { recipient.UserId, recipient.ReadAt })
                .HasDatabaseName("ix_recipient_unread");

            entity.HasOne(recipient => recipient.Announcement)
                .WithMany(announcement => announcement.Recipients)
                .HasForeignKey(recipient => recipient.AnnouncementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(recipient => recipient.User)
                .WithMany()
                .HasForeignKey(recipient => recipient.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<PermissionForm>(entity =>
        {
            entity.ToTable("permission_forms", table => table.HasCheckConstraint(
                "ck_pf_target",
                // Exactly one of the two, never both and never neither. A form
                // attached to nothing is a form nobody can be asked about; a
                // form attached to both is a form with two audiences.
                "([event_id] IS NOT NULL AND [programme_id] IS NULL) "
                + "OR ([event_id] IS NULL AND [programme_id] IS NOT NULL)"));

            entity.HasKey(form => form.Id);
            entity.Property(form => form.Title).HasMaxLength(200).IsRequired();
            entity.Property(form => form.Body).HasMaxLength(4000).IsRequired();

            entity.HasOne(form => form.Event)
                .WithMany()
                .HasForeignKey(form => form.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(form => form.Programme)
                .WithMany()
                .HasForeignKey(form => form.ProgrammeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Consent>(entity =>
        {
            // Append-only, enforced by the database: an INSTEAD OF trigger
            // refuses every UPDATE and DELETE (migration
            // EnforceRecordIntegrity). Declared here so EF Core does not use
            // an OUTPUT clause on insert, which SQL Server rejects on a table
            // with a trigger.
            entity.ToTable("consents", table => table.HasTrigger("trg_consents_append_only"));
            entity.HasKey(consent => consent.Id);
            entity.Property(consent => consent.SignatureRef).HasMaxLength(128).IsRequired();
            entity.Property(consent => consent.IpAddress).HasMaxLength(45);

            // ================================================================
            //  The monotonic sequence, and why it is not a timestamp.
            //
            //  This column exists because of a bug found by driving the live
            //  API: signed_at had one-second resolution, so a parent who
            //  granted and then immediately withdrew produced two rows with
            //  identical timestamps. MAX(signed_at) tied, the "current consent"
            //  query returned two rows, and which decision counted as current
            //  was whatever the storage engine happened to return first.
            //
            //  A timestamp cannot order an append-only log, because two events
            //  can share one. An identity column can, always.
            // ================================================================
            entity.Property(consent => consent.Sequence).ValueGeneratedOnAdd().UseIdentityColumn();

            entity.HasIndex(consent => new { consent.FormId, consent.LearnerId, consent.Sequence })
                .HasDatabaseName("ix_consent_current");

            // Restrict, not cascade. A cascade here meant deleting a permission
            // form deleted every parent's decision on it — the consent history
            // that POPIA requires kept, and that the privacy policy promises
            // survives a withdrawal. A form that has been answered is a record,
            // not a draft, and it cannot be deleted out from under its answers.
            entity.HasOne(consent => consent.Form)
                .WithMany(form => form.Consents)
                .HasForeignKey(consent => consent.FormId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(consent => consent.Learner)
                .WithMany()
                .HasForeignKey(consent => consent.LearnerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Wired to the named collection, not left as .WithMany(). Leaving it
            // unwired makes EF treat ParentGuardian.Consents as a *second*
            // relationship and invent a shadow parent_guardian_id column beside
            // parent_id — two foreign keys to the same table, one of which
            // nothing ever populates.
            entity.HasOne(consent => consent.Parent)
                .WithMany(parent => parent.Consents)
                .HasForeignKey(consent => consent.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureCrossCutting(ModelBuilder model)
    {
        model.Entity<AuditLogEntry>(entity =>
        {
            // Immutable, enforced by the database rather than by good
            // behaviour: an INSTEAD OF trigger refuses every UPDATE and
            // DELETE, including from an administrator with a SQL client.
            entity.ToTable("audit_log", table => table.HasTrigger("trg_audit_log_immutable"));
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Action).HasMaxLength(80).IsRequired();
            entity.Property(audit => audit.EntityName).HasMaxLength(60).IsRequired();
            entity.Property(audit => audit.EntityId).HasMaxLength(60);
            entity.Property(audit => audit.IpAddress).HasMaxLength(45);
            entity.Property(audit => audit.UserAgent).HasMaxLength(255);

            entity.HasIndex(audit => new { audit.EntityName, audit.EntityId })
                .HasDatabaseName("ix_audit_entity");

            entity.HasIndex(audit => audit.OccurredAt).HasDatabaseName("ix_audit_time");

            // No foreign key to users, deliberately. An audit trail that can be
            // erased by deleting an account is not an audit trail — and with a
            // FK, even ON DELETE SET NULL would silently detach every entry a
            // departing administrator wrote. The actor id is kept as a plain
            // value and resolved to an email at read time when the user still
            // exists.
            entity.Property(audit => audit.UserId);
        });

        model.Entity<NotificationPreference>(entity =>
        {
            entity.ToTable("notification_preferences");
            entity.HasKey(preference => new { preference.UserId, preference.Category });

            entity.HasOne(preference => preference.User)
                .WithMany(user => user.NotificationPreferences)
                .HasForeignKey(preference => preference.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(notification => notification.Id);
            entity.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
            entity.Property(notification => notification.Body).HasMaxLength(500).IsRequired();
            entity.Property(notification => notification.Icon).HasMaxLength(8);
            entity.Property(notification => notification.Link).HasMaxLength(255);

            // Filtered on unread, because "how many unread notifications" is
            // asked on every dashboard load and the unread set is a small
            // fraction of the table.
            entity.HasIndex(notification => new { notification.UserId, notification.CreatedAt })
                .HasFilter("[read_at] IS NULL")
                .HasDatabaseName("ix_notification_unread");

            entity.HasOne(notification => notification.User)
                .WithMany()
                .HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<NewsPost>(entity =>
        {
            entity.ToTable("news_posts");
            entity.HasKey(post => post.Id);
            entity.Property(post => post.Slug).HasMaxLength(80).IsRequired();
            entity.Property(post => post.Title).HasMaxLength(200).IsRequired();
            entity.Property(post => post.Excerpt).HasMaxLength(500).IsRequired();
            entity.Property(post => post.BodyHtml).IsRequired();
            entity.Property(post => post.AuthorName).HasMaxLength(140).IsRequired();

            entity.HasIndex(post => post.Slug).IsUnique().HasDatabaseName("uk_news_slug");

            entity.HasIndex(post => new { post.Category, post.PublishedAt })
                .HasDatabaseName("ix_news_published");
        });

        model.Entity<ContactMessage>(entity =>
        {
            entity.ToTable("contact_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Name).HasMaxLength(140).IsRequired();
            entity.Property(message => message.Organisation).HasMaxLength(140);
            entity.Property(message => message.Email).HasMaxLength(190).IsRequired();
            entity.Property(message => message.Phone).HasMaxLength(30);
            entity.Property(message => message.Body).HasMaxLength(4000).IsRequired();
            entity.Property(message => message.IpAddress).HasMaxLength(45);

            entity.HasIndex(message => new { message.Status, message.CreatedAt })
                .HasDatabaseName("ix_contact_status");
        });

        model.Entity<NewsletterSubscriber>(entity =>
        {
            entity.ToTable("newsletter_subscribers");
            entity.HasKey(subscriber => subscriber.Id);
            entity.Property(subscriber => subscriber.Email).HasMaxLength(190).IsRequired();

            entity.Property(subscriber => subscriber.Email).UseCollation("Latin1_General_CI_AS");
            entity.HasIndex(subscriber => subscriber.Email)
                .IsUnique()
                .HasDatabaseName("uk_news_sub_email");
        });

        model.Entity<ImpactMetric>(entity =>
        {
            entity.ToTable("impact_metrics");

            // Keyed on the metric and the year, which is what makes the refresh
            // an upsert. A surrogate key would let one refresh insert a second
            // "learners_supported" for 2026 beside the first, and the page would
            // then show whichever the query happened to order first.
            entity.HasKey(metric => new { metric.MetricKey, metric.SchoolYear });

            entity.Property(metric => metric.MetricKey).HasMaxLength(40);
            entity.Property(metric => metric.Label).HasMaxLength(120).IsRequired();
            entity.Property(metric => metric.ValueText).HasMaxLength(40).IsRequired();
            entity.Property(metric => metric.Footnote).HasMaxLength(255);
        });
    }
}
