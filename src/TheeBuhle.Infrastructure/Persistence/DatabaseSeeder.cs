// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) Entity Framework Core documentation.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Entities;
using TheeBuhle.Domain.Repositories;

namespace TheeBuhle.Infrastructure.Persistence;

/// <summary>
/// Seeds a database with realistic demonstration data.
/// </summary>
/// <remarks>
/// <para>
/// The programme, venue and badge content is the copy from the
/// <strong>client-approved Task 1 prototype</strong>, not invented filler. The
/// point of that is the demonstration: a marker or a client opening the app sees
/// the words they signed off, and the numbers on the impact page are computed
/// from data that is internally consistent rather than typed in.
/// </para>
/// <para>
/// <strong>Idempotent.</strong> It checks whether the programmes already exist
/// and does nothing if they do, so it can run on every startup in development
/// without accumulating a second copy of everything.
/// </para>
/// <para>
/// Two things this seeder deliberately does <em>not</em> do:
/// </para>
/// <list type="bullet">
/// <item>
/// It does not hand-write badge awards. It runs the real
/// <see cref="BadgeEvaluator"/> against the seeded attendance, so the badges a
/// learner holds are ones the live rules actually award — if the evaluator is
/// wrong, the demonstration is wrong in the same way, which is the point.
/// </item>
/// <item>
/// It does not create a flagged safeguarding concern. Seeding one would put
/// invented text about a child in the database and, worse, would exercise the
/// escalation chain outside a request. The safeguarding path is demonstrated
/// live instead.
/// </item>
/// </list>
/// </remarks>
public sealed partial class DatabaseSeeder(
    TheeBuhleDbContext context,
    IPasswordHasher passwordHasher,
    BadgeEvaluator badgeEvaluator,
    ImpactService impact,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>
    /// The password every demonstration account shares.
    /// </summary>
    /// <remarks>
    /// One known password across the seeded accounts, so a marker can sign in as
    /// each role without being handed a list. It is only ever set by this
    /// seeder, which runs in development — and it is long enough to satisfy the
    /// change-password rule so that an account created here can be used to
    /// exercise that endpoint too.
    /// </remarks>
    public const string DemoPassword = "Vanderbijlpark2026!";

    /// <summary>Seeds a development database, with <see cref="DemoPassword"/>.</summary>
    public Task SeedAsync(CancellationToken cancellationToken = default) =>
        SeedAsync(DemoPassword, cancellationToken);

    /// <summary>
    /// Seeds with a password chosen by whoever runs it.
    /// </summary>
    /// <remarks>
    /// Used by the <c>seed-demo</c> command against a hosted database. The
    /// password in <see cref="DemoPassword"/> is printed in the README, so a
    /// public site seeded with it would have a staff console anybody could sign
    /// in to.
    /// </remarks>
    public async Task SeedAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (await context.Programmes.AnyAsync(cancellationToken))
        {
            SeedLog.AlreadySeeded(logger);

            // The core seed ran long ago; the demonstration extras are
            // additive and may not have. They run on every start and add only
            // what is missing, so a developer's existing database picks them up
            // without being dropped — and nothing is ever inserted twice.
            await SeedDemonstrationExtrasAsync(cancellationToken);

            return;
        }

        var hash = passwordHasher.Hash(password);

        await unitOfWork.TransactionAsync(async () =>
        {
            var venues = SeedVenues();
            var programmes = SeedProgrammes();
            var subjects = SeedSubjects();

            SeedBadges();

            var staff = SeedStaff(hash);
            var mentors = SeedMentors(hash);
            var learners = SeedLearners(hash, programmes);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            SeedGuardians(hash, learners);
            // Marks and registers are attributed to the programme manager,
            // who is who captures them in real life. An invented identifier
            // here once left seventy register entries recorded by nobody,
            // which the Impact page's method statement says cannot happen.
            var capturer = staff["manager"].User.Id;

            SeedMarks(learners, subjects, capturer);
            SeedMentorAssignments(mentors, learners, staff);

            var sessions = SeedSessions(programmes, venues);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            SeedAttendance(sessions, learners, capturer);
            SeedContent(venues);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        // Badges are awarded by the real evaluator against the seeded
        // attendance, outside the seeding transaction — so what a learner holds
        // is what the live rules award rather than what a seeder decided.
        await AwardBadgesAsync(cancellationToken);

        // The published impact figures, computed by the real service.
        //
        // Not optional. The homepage's headline ribbon and the whole Impact page
        // read the *stored* metrics rather than aggregating live — which is the
        // right design, and it means a database with attendance in it but no
        // published metrics renders a homepage with em-dashes where the numbers
        // should be. Seeding the data those figures are computed from without
        // computing them leaves the site looking broken on a fresh deployment.
        //
        // Run through ImpactService for the same reason the badges go through
        // the evaluator: the demonstration must not disagree with the system it
        // is demonstrating.
        await impact.RefreshAsync(cancellationToken: cancellationToken);

        await SeedDemonstrationExtrasAsync(cancellationToken);

        // Counted into locals first. Passing the awaited queries directly as
        // log arguments runs three round trips even when the log level would
        // discard the message — which the analyser is right to object to.
        var learnerCount = await context.Learners.CountAsync(cancellationToken);
        var attendanceCount = await context.Attendance.CountAsync(cancellationToken);
        var badgeCount = await context.LearnerBadges.CountAsync(cancellationToken);

        var metricCount = await context.ImpactMetrics.CountAsync(cancellationToken);

        SeedLog.Seeded(logger, learnerCount, attendanceCount, badgeCount, metricCount);
    }

    private Dictionary<string, Venue> SeedVenues()
    {
        var venues = new Dictionary<string, Venue>(StringComparer.Ordinal)
        {
            ["sebokeng"] = new()
            {
                Name = "Sebokeng Community Centre",
                Address = "Moshoeshoe Street, Sebokeng Zone 12, 1983",
                Capacity = 90,
            },
            ["vanderbijl"] = new()
            {
                Name = "Thee Buhle Offices, Vanderbijlpark",
                Address = "Cornwallis Street S.E.7, Vanderbijlpark, 1911",
                Capacity = 40,
            },
            ["bophelong"] = new()
            {
                Name = "Bophelong Community Hall",
                Address = "Moshoeshoe Road, Bophelong, 1911",
                Capacity = 120,
            },
        };

        context.Venues.AddRange(venues.Values);

        return venues;
    }

    /// <summary>
    /// The four programmes, with the copy from the client-approved prototype.
    /// </summary>
    private Dictionary<string, Programme> SeedProgrammes()
    {
        var programmes = new Dictionary<string, Programme>(StringComparer.Ordinal)
        {
            ["tutoring"] = new()
            {
                Slug = "tutoring",
                Number = "01",
                Name = "Tutoring & Academic Support",
                Summary = "Saturday morning sessions in small groups, working through the CAPS "
                    + "curriculum their teachers are on that week.",
                Description = "Saturday morning sessions at the Sebokeng and Vanderbijlpark centres. "
                    + "Small groups — never more than six learners per tutor — working through the actual "
                    + "CAPS curriculum their teachers are on that week, not a parallel syllabus.",
                Expectations =
                [
                    "09h00 – 12h30 every Saturday during term",
                    "Maths, Physical Science, Accounting, English",
                    "Past papers and exam technique from Grade 11",
                    "A quiet, supervised space with Wi-Fi and stationery",
                ],
                HowToJoin = "Register online or walk in with a parent on any Saturday.",
                CtaLabel = "Register a learner",
                Audience = "Grades 8–12",
                CapacityNote = "34 open places",
                MinGrade = 8,
                MaxGrade = 12,
                SortOrder = 1,
            },
            ["mentorship"] = new()
            {
                Slug = "mentorship",
                Number = "02",
                Name = "Mentorship Programme",
                Summary = "Every learner is matched with a vetted adult mentor who commits to a full school year.",
                Description = "Every learner in our programmes is matched with a vetted adult mentor who "
                    + "commits to a full school year. Mentors check in weekly — in person, by WhatsApp, or "
                    + "through the app — and log every session so nothing falls through the cracks.",
                Expectations =
                [
                    "One 45-minute check-in every week",
                    "Same mentor for the full year",
                    "Goal-setting each term with the learner and parent",
                    "Escalation to staff when a learner needs more support",
                ],
                HowToJoin = "Learners are matched at the start of each term. Mentors apply through the volunteer form.",
                CtaLabel = "Apply to be a mentor",
                Audience = "Ages 13–19",
                CapacityNote = "12 learners waiting for a match",
                MinGrade = 7,
                MaxGrade = 12,
                SortOrder = 2,
            },
            ["lifeskills"] = new()
            {
                Slug = "lifeskills",
                Number = "03",
                Name = "Social & Life-Skills Workshops",
                Summary = "The things school assumes somebody else will teach — money, CVs, "
                    + "digital safety, public speaking.",
                Description = "The things school assumes someone else will teach. Run monthly on Saturday "
                    + "afternoons, open to any young person in the community whether or not they attend our "
                    + "tutoring.",
                Expectations =
                [
                    "CV writing and interview practice",
                    "Money basics: budgeting, banking, first payslip",
                    "Digital safety and online reputation",
                    "Public speaking and conflict resolution",
                ],
                HowToJoin = "Free and open. Register per workshop on the Events page.",
                CtaLabel = "Register a learner",
                Audience = "All ages",
                CapacityNote = "Next: 12 September",
                MinGrade = 4,
                MaxGrade = 12,
                SortOrder = 3,
            },
            ["camps"] = new()
            {
                Slug = "camps",
                Number = "04",
                Name = "Holiday & School-Break Camps",
                Summary = "Four-day day-camps over the June and December breaks — catch-up work, "
                    + "sport, reading and a hot meal.",
                Description = "Four-day day-camps over the June and December breaks. Half the day is "
                    + "structured catch-up work, half is sport, reading and mentoring. Every child gets a hot "
                    + "meal, and transport is arranged from three pick-up points.",
                Expectations =
                [
                    "Four consecutive days, 08h00 – 15h00",
                    "Hot lunch and transport included",
                    "Reading circles and a take-home book",
                    "Parents invited to the closing showcase",
                ],
                HowToJoin = "Places are limited — priority to learners already in a programme.",
                CtaLabel = "Register a learner",
                Audience = "Grades 4–12",
                CapacityNote = "June 2027 opens 1 April",
                MinGrade = 4,
                MaxGrade = 12,
                SortOrder = 4,
            },
        };

        context.Programmes.AddRange(programmes.Values);

        return programmes;
    }

    private Dictionary<string, Subject> SeedSubjects()
    {
        (string Name, string Icon, string Accent)[] specs =
        [
            ("Mathematics", "∑", "#2E4034"),
            ("Physical Science", "⚛", "#A87C32"),
            ("Accounting", "₣", "#6B4F3A"),
            ("English HL", "✎", "#3A4A55"),
            ("Life Sciences", "❦", "#5C6B4F"),
            ("Life Orientation", "◈", "#8B7A55"),
        ];

        var subjects = new Dictionary<string, Subject>(StringComparer.Ordinal);

        foreach (var grade in new[] { 8, 9, 10, 11, 12 })
        {
            foreach (var (name, icon, accent) in specs)
            {
                var subject = new Subject
                {
                    Name = name,
                    Grade = grade,
                    Icon = icon,
                    Accent = accent,
                };

                context.Subjects.Add(subject);
                subjects[$"{grade}|{name}"] = subject;
            }
        }

        return subjects;
    }

    /// <summary>
    /// The badge catalogue.
    /// </summary>
    /// <remarks>
    /// Criteria are <em>data, not code</em> — the criteria type and threshold
    /// are columns, so a staff member can add "Steady hand at six sessions"
    /// without a deployment. That is why this is seed data rather than a static
    /// list in the evaluator.
    /// </remarks>
    private void SeedBadges()
    {
        (string Code, string Name, string Description, string Icon, BadgeCriteria Criteria, int Threshold, int Points)[] specs =
        [
            ("perfect_month", "Perfect month", "Attend every session in a calendar month.", "◉", BadgeCriteria.AttendanceStreak, 4, 150),
            ("steady_hand", "Steady hand", "Attend four sessions in a row.", "✦", BadgeCriteria.AttendanceStreak, 4, 100),
            ("long_haul", "Long haul", "Attend twenty-five sessions in a school year.", "◈", BadgeCriteria.SessionsAttended, 25, 250),
            ("first_ten", "First ten", "Attend your first ten sessions.", "⌂", BadgeCriteria.SessionsAttended, 10, 100),
            ("ten_books", "10 resources read", "Work through ten learning resources.", "▤", BadgeCriteria.ResourcesCompleted, 10, 150),
            ("first_resource", "Getting started", "Complete your first learning resource.", "▥", BadgeCriteria.ResourcesCompleted, 1, 50),
            ("big_jump", "Big jump", "Improve a subject mark by twenty points or more.", "↗", BadgeCriteria.MarkImprovement, 20, 300),
            ("on_the_up", "On the up", "Improve a subject mark by ten points.", "▲", BadgeCriteria.MarkImprovement, 10, 150),
        ];

        foreach (var (code, name, description, icon, criteria, threshold, points) in specs)
        {
            context.Badges.Add(new Badge
            {
                Code = code,
                Name = name,
                Description = description,
                Icon = icon,
                Criteria = criteria,
                Threshold = threshold,
                Points = points,
            });
        }
    }

    private Dictionary<string, StaffMember> SeedStaff(string hash)
    {
        var staff = new Dictionary<string, StaffMember>(StringComparer.Ordinal);

        (string Key, string Email, string First, string Surname, string Title, bool IsLead, Role Role)[] specs =
        [
            ("admin", "admin@theebuhlefoundation.co.za", "Buhle", "Ndlovu", "Director", false, Role.Admin),

            // Exactly one safeguarding lead. The filtered unique index on the
            // staff table refuses a second, so this seeder could not create two
            // even by mistake.
            ("safeguarding", "safeguarding@theebuhlefoundation.co.za", "Nomsa", "Dube", "Safeguarding Lead", true, Role.Staff),

            // The two roles the escalation chain routes to, matched on job
            // title. Without them the chain falls through to the lead, which is
            // correct but makes links 1 and 2 untestable.
            ("mentorlead", "mentorlead@theebuhlefoundation.co.za", "Sipho", "Maseko", "Mentor Lead", false, Role.Staff),
            ("manager", "programmes@theebuhlefoundation.co.za", "Lerato", "Khumalo", "Programme Manager", false, Role.Staff),
        ];

        foreach (var (key, email, first, surname, title, isLead, role) in specs)
        {
            var user = new User
            {
                Email = email,
                PasswordHash = hash,
                Role = role,
                Status = UserStatus.Active,
            };

            context.Users.Add(user);

            var member = new StaffMember
            {
                User = user,
                FirstName = first,
                Surname = surname,
                JobTitle = title,
                IsSafeguardingLead = isLead,
            };

            context.Staff.Add(member);
            staff[key] = member;
        }

        return staff;
    }

    private Dictionary<string, Mentor> SeedMentors(string hash)
    {
        var mentors = new Dictionary<string, Mentor>(StringComparer.Ordinal);

        (string Key, string Email, string First, string Surname, string Occupation, int ExpiryMonths)[] specs =
        [
            ("thandi", "thandi.mokoena@example.co.za", "Thandi", "Mokoena", "Civil engineer", 14),
            ("pieter", "pieter.venter@example.co.za", "Pieter", "Venter", "Accountant", 9),

            // A mentor whose clearance expires next month. Seeded on purpose:
            // it is the case a CHECK constraint cannot catch, and it is what
            // makes MentorshipService.AssertCleared demonstrable.
            ("naledi", "naledi.sithole@example.co.za", "Naledi", "Sithole", "Teacher", 1),
        ];

        foreach (var (key, email, first, surname, occupation, expiryMonths) in specs)
        {
            var user = new User
            {
                Email = email,
                PasswordHash = hash,
                Role = Role.Mentor,
                Status = UserStatus.Active,
            };

            context.Users.Add(user);

            var mentor = new Mentor
            {
                User = user,
                FirstName = first,
                Surname = surname,
                Occupation = occupation,
                ClearanceStatus = ClearanceStatus.Cleared,
                ClearanceExpiry = clock.Today.AddMonths(expiryMonths),
            };

            context.Mentors.Add(mentor);
            mentors[key] = mentor;
        }

        return mentors;
    }

    private Dictionary<string, Learner> SeedLearners(string hash, Dictionary<string, Programme> programmes)
    {
        var learners = new Dictionary<string, Learner>(StringComparer.Ordinal);
        var term = $"{clock.Today.Year}-3";

        (string Reference, string First, string Surname, int Grade, string School, string Suburb, string Ambition, bool HasLogin)[] specs =
        [
            ("A-214", "Lebo", "Mahlangu", 10, "Sebokeng Secondary", "Sebokeng", "Wants to study civil engineering", true),
            ("A-227", "Karabo", "Nkosi", 11, "Vanderbijlpark High", "Vanderbijlpark", "Wants to be a journalist", true),
            ("A-231", "Ayanda", "Radebe", 9, "Bophelong Secondary", "Bophelong", "Wants to run her own business", false),
            ("A-244", "Tumi", "Molefe", 12, "Sebokeng Secondary", "Sebokeng", "Wants to study medicine", true),
            ("A-251", "Sibusiso", "Zulu", 8, "Bophelong Secondary", "Bophelong", "Wants to be an electrician", false),
            ("A-263", "Palesa", "Mothibi", 10, "Vanderbijlpark High", "Vanderbijlpark", "Wants to study architecture", false),
        ];

        foreach (var (reference, first, surname, grade, school, suburb, ambition, hasLogin) in specs)
        {
            User? user = null;

            if (hasLogin)
            {
                user = new User
                {
                    Email = $"{first.ToLowerInvariant()}.{surname.ToLowerInvariant()}@learner.example",
                    PasswordHash = hash,
                    Role = Role.Learner,
                    Status = UserStatus.Active,
                };

                context.Users.Add(user);
            }

            var learner = new Learner
            {
                ReferenceCode = reference,
                FirstName = first,
                Surname = surname,
                // Roughly right for the grade. Not a real date of birth, because
                // this is demonstration data about invented children.
                DateOfBirth = clock.Today.AddYears(-(grade + 6)).AddDays(-40),
                Grade = grade,
                School = school,
                Suburb = suburb,
                Ambition = ambition,
                User = user,
            };

            context.Learners.Add(learner);
            learners[reference] = learner;

            // Everybody is enrolled in tutoring; the older ones are also on the
            // mentorship programme, which is what the prototype shows.
            context.Enrolments.Add(new Enrolment
            {
                Learner = learner,
                Programme = programmes["tutoring"],
                Term = term,
                Status = EnrolmentStatus.Active,
            });

            if (grade >= 9)
            {
                context.Enrolments.Add(new Enrolment
                {
                    Learner = learner,
                    Programme = programmes["mentorship"],
                    Term = term,
                    Status = EnrolmentStatus.Active,
                });
            }
        }

        return learners;
    }

    private void SeedGuardians(string hash, Dictionary<string, Learner> learners)
    {
        (string Email, string First, string Surname, string Phone, string[] Children)[] specs =
        [
            ("nomvula.mahlangu@example.co.za", "Nomvula", "Mahlangu", "+27 82 555 0101", ["A-214"]),
            ("joseph.nkosi@example.co.za", "Joseph", "Nkosi", "+27 82 555 0102", ["A-227"]),

            // One guardian with two children, so the dashboard's learner chooser
            // is exercised rather than always defaulting silently.
            ("grace.radebe@example.co.za", "Grace", "Radebe", "+27 82 555 0103", ["A-231", "A-251"]),
            ("elizabeth.molefe@example.co.za", "Elizabeth", "Molefe", "+27 82 555 0104", ["A-244"]),
            ("thabo.mothibi@example.co.za", "Thabo", "Mothibi", "+27 82 555 0105", ["A-263"]),
        ];

        foreach (var (email, first, surname, phone, children) in specs)
        {
            var user = new User
            {
                Email = email,
                PasswordHash = hash,
                Role = Role.Parent,
                Status = UserStatus.Active,
            };

            context.Users.Add(user);

            var parent = new ParentGuardian
            {
                User = user,
                FirstName = first,
                Surname = surname,
                Phone = phone,
                Relationship = "guardian",
                ContactConfirmedAt = clock.Today.AddMonths(-2),
            };

            context.ParentGuardians.Add(parent);

            for (var i = 0; i < children.Length; i++)
            {
                context.GuardianLinks.Add(new GuardianLink
                {
                    Parent = parent,
                    Learner = learners[children[i]],
                    IsPrimary = i == 0,
                });
            }
        }
    }

    /// <summary>
    /// School-report marks, one per term.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three terms each, so the mark-trend query has a real series to order by
    /// term rather than a pair it could get right by accident.
    /// </para>
    /// <para>
    /// A-263's Mathematics marks go <em>down</em> — 58, 64, 51. That is
    /// deliberate: it is the case that exposes the difference between computing
    /// improvement by term and computing it as max-minus-min, which would report
    /// this learner as having improved by six when they have fallen by seven.
    /// </para>
    /// </remarks>
    private void SeedMarks(Dictionary<string, Learner> learners, Dictionary<string, Subject> subjects, Guid capturedBy)
    {
        var year = clock.Today.Year;

        (string Reference, string Subject, int[] Marks)[] specs =
        [
            ("A-214", "Mathematics", [42, 58, 71]),
            ("A-214", "Physical Science", [48, 57, 66]),
            ("A-214", "Accounting", [55, 59, 64]),
            ("A-227", "English HL", [68, 74, 81]),
            ("A-227", "Mathematics", [51, 57, 63]),
            ("A-231", "Accounting", [49, 55, 62]),
            ("A-231", "Mathematics", [44, 49, 55]),
            ("A-244", "Life Sciences", [57, 64, 72]),
            ("A-244", "Mathematics", [46, 52, 58]),

            // The learner whose marks fell.
            ("A-263", "Mathematics", [58, 64, 51]),
            ("A-263", "English HL", [61, 60, 59]),
        ];

        foreach (var (reference, subjectName, marks) in specs)
        {
            var learner = learners[reference];

            if (!subjects.TryGetValue($"{learner.Grade}|{subjectName}", out var subject))
            {
                continue;
            }

            for (var term = 0; term < marks.Length; term++)
            {
                context.AssessmentResults.Add(new AssessmentResult
                {
                    Learner = learner,
                    Subject = subject,
                    Term = $"{year}-{term + 1}",
                    Mark = marks[term],
                    Source = MarkSource.SchoolReport,
                    CapturedBy = capturedBy,
                });
            }
        }
    }

    private void SeedMentorAssignments(
        Dictionary<string, Mentor> mentors,
        Dictionary<string, Learner> learners,
        Dictionary<string, StaffMember> staff)
    {
        var approver = staff["admin"].UserId;
        var year = clock.Today.Year;

        (string Mentor, string Learner)[] pairs =
        [
            ("thandi", "A-214"),
            ("thandi", "A-227"),
            ("pieter", "A-244"),
            ("pieter", "A-231"),
        ];

        foreach (var (mentorKey, reference) in pairs)
        {
            context.MentorAssignments.Add(new MentorAssignment
            {
                Mentor = mentors[mentorKey],
                Learner = learners[reference],
                SchoolYear = year,
                Status = AssignmentStatus.Active,
                ApprovedBy = approver,
            });
        }
    }

    /// <summary>
    /// Twelve Saturdays of sessions, plus one still to come.
    /// </summary>
    /// <remarks>
    /// Anchored to the most recent Saturday and counted backwards, so the seed
    /// data is always relative to today rather than to whenever the seeder was
    /// written. The upcoming session matters: without one, the dashboard's "next
    /// session" card is empty and looks broken rather than seeded.
    /// </remarks>
    private List<TutoringSession> SeedSessions(
        Dictionary<string, Programme> programmes,
        Dictionary<string, Venue> venues)
    {
        var sessions = new List<TutoringSession>();

        var today = clock.Today;
        var daysSinceSaturday = ((int)today.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7;
        var lastSaturday = today.AddDays(-daysSinceSaturday);

        for (var week = 11; week >= 0; week--)
        {
            var date = lastSaturday.AddDays(-7 * week);

            var session = new TutoringSession
            {
                Programme = programmes["tutoring"],
                Venue = venues[(week % 2) == 0 ? "sebokeng" : "vanderbijl"],
                StartsAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(2)),
                EndsAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 30)), TimeSpan.FromHours(2)),
                // Parenthesised because a switch expression binds tighter than
                // %, so `week % 3 switch {...}` parses as `week % (3 switch
                // {...})` — an int modulo a string. Worth the brackets.
                SubjectFocus = (week % 3) switch
                {
                    0 => "Mathematics · Euclidean geometry",
                    1 => "Physical Science · Chemical equations",
                    _ => "Accounting · Ledger accounts",
                },
                Room = "Hall A",
                Capacity = 40,
            };

            context.Sessions.Add(session);
            sessions.Add(session);
        }

        // Next Saturday, so the parent dashboard has a "next session" card.
        var upcoming = lastSaturday.AddDays(7);

        context.Sessions.Add(new TutoringSession
        {
            Programme = programmes["tutoring"],
            Venue = venues["sebokeng"],
            StartsAt = new DateTimeOffset(upcoming.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(2)),
            EndsAt = new DateTimeOffset(upcoming.ToDateTime(new TimeOnly(12, 30)), TimeSpan.FromHours(2)),
            SubjectFocus = "Mathematics · Trigonometry",
            Room = "Hall A",
            Capacity = 40,
        });

        return sessions;
    }

    /// <summary>
    /// Attendance across the twelve past sessions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately uneven. A-214 attends everything, which earns the streak
    /// badges; A-263 misses several, which gives the attendance rate something
    /// other than 100% to report and gives a parent an absence to see.
    /// </para>
    /// <para>
    /// The <em>last</em> session is left uncaptured for two learners, so the
    /// console's "registers outstanding" view has something in it — which is the
    /// state that view exists to surface.
    /// </para>
    /// </remarks>
    private void SeedAttendance(List<TutoringSession> sessions, Dictionary<string, Learner> learners, Guid recordedBy)
    {

        // Which sessions each learner missed, by index from the oldest.
        var absences = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["A-214"] = [],
            ["A-227"] = [2],
            ["A-231"] = [0, 5],
            ["A-244"] = [7],
            ["A-251"] = [1, 4, 8, 9],
            ["A-263"] = [0, 3, 6, 10],
        };

        // Late arrivals, which count as attended — the taxi, not a choice.
        var lates = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["A-227"] = [6],
            ["A-251"] = [3],
            ["A-263"] = [8],
        };

        foreach (var (reference, learner) in learners)
        {
            for (var index = 0; index < sessions.Count; index++)
            {
                // The most recent register is left half-captured on purpose.
                if (index == sessions.Count - 1 && reference is "A-251" or "A-263")
                {
                    continue;
                }

                var status = absences[reference].Contains(index)
                    ? AttendanceStatus.Absent
                    : lates.GetValueOrDefault(reference, []).Contains(index)
                        ? AttendanceStatus.Late
                        : AttendanceStatus.Present;

                context.Attendance.Add(new AttendanceRecord
                {
                    Session = sessions[index],
                    Learner = learner,
                    Status = status,
                    ArrivedAt = status == AttendanceStatus.Late
                        ? sessions[index].StartsAt.AddMinutes(25)
                        : status == AttendanceStatus.Present
                            ? sessions[index].StartsAt
                            : null,
                    Note = status == AttendanceStatus.Absent && index % 2 == 0
                        ? "Parent phoned — taxi fare"
                        : null,
                    RecordedBy = recordedBy,
                });
            }
        }
    }

    private void SeedContent(Dictionary<string, Venue> venues)
    {
        var now = clock.Now;

        context.NewsPosts.Add(new NewsPost
        {
            Slug = "term-3-two-venues",
            Category = NewsCategory.ProgrammeUpdate,
            Title = "Term 3 tutoring moves to two venues",
            Excerpt = "From this term, Saturday tutoring runs at both Sebokeng Community Centre and "
                + "our Vanderbijlpark offices — which means smaller groups and a shorter walk for most families.",
            BodyHtml = "<p>From this term, Saturday tutoring runs at both Sebokeng Community Centre and "
                + "our Vanderbijlpark offices.</p><p>Two venues means smaller groups — we are holding to "
                + "six learners per tutor — and a shorter walk for most families. Learners keep the same "
                + "tutor and the same mentor; only the room changes.</p>",
            AuthorName = "Buhle Ndlovu",
            ReadMinutes = 3,
            IsFeatured = true,
            PublishedAt = now.AddDays(-9),
        });

        context.NewsPosts.Add(new NewsPost
        {
            Slug = "mentors-needed-2026",
            Category = NewsCategory.Community,
            Title = "We need twelve more mentors",
            Excerpt = "Twelve learners are waiting for a match. A mentor commits to one 45-minute "
                + "conversation a week for a school year — and we handle the police clearance for you.",
            BodyHtml = "<p>Twelve learners are currently waiting for a mentor.</p><p>A mentor commits to "
                + "one 45-minute conversation a week for a full school year. You do not need to be a "
                + "teacher. We arrange the SAPS clearance, run the safeguarding induction, and you are "
                + "never alone with a learner.</p>",
            AuthorName = "Sipho Maseko",
            ReadMinutes = 4,
            PublishedAt = now.AddDays(-23),
        });

        context.Events.Add(new PublicEvent
        {
            Title = "5KM Social Run for Education",
            Slug = "5km-social-run",
            Type = EventType.Fundraising,
            Venue = venues["vanderbijl"],
            Location = "Vaal River waterfront, Vanderbijlpark",
            StartsAt = now.AddDays(26).Date.AddHours(7).ToUniversalTime(),
            EndsAt = now.AddDays(26).Date.AddHours(11).ToUniversalTime(),
            Fee = 80m,
            FeeNote = "R80 per entry, R200 for a family of four. Every rand goes to stationery.",
            Capacity = 250,
            Description = "A flat, easy 5km along the river. Walk it, run it, or push a pram round it. "
                + "T-shirts for the first 100 entries.",
        });

        context.Events.Add(new PublicEvent
        {
            Title = "June Holiday Camp",
            Slug = "june-holiday-camp",
            // The camp is a Community event: the enum has no Camp member,
            // and a four-day day-camp for local learners is community work
            // rather than fundraising or a workshop.
            Type = EventType.Community,
            Venue = venues["bophelong"],
            Location = "Bophelong Community Hall",
            StartsAt = now.AddDays(54).Date.AddHours(8).ToUniversalTime(),
            EndsAt = now.AddDays(57).Date.AddHours(15).ToUniversalTime(),
            Fee = 0m,
            FeeNote = "Free. Hot lunch and transport included.",
            Capacity = 120,
            Description = "Four days of catch-up work, sport, reading and mentoring. Transport from three "
                + "pick-up points.",
        });
    }

    /// <summary>
    /// Awards badges by running the real evaluator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not hand-written rows. The evaluator is the same one the attendance
    /// listener calls, so the badges a seeded learner holds are exactly the ones
    /// the live rules award for that attendance and those marks.
    /// </para>
    /// <para>
    /// That is worth the extra queries: hand-writing the awards would make the
    /// demonstration data agree with what somebody <em>thought</em> the rules
    /// did, and the one thing a demonstration must not do is disagree with the
    /// system it is demonstrating.
    /// </para>
    /// </remarks>
    private async Task AwardBadgesAsync(CancellationToken cancellationToken)
    {
        var learnerIds = await context.Learners
            .Select(learner => learner.Id)
            .ToListAsync(cancellationToken);

        foreach (var learnerId in learnerIds)
        {
            await badgeEvaluator.EvaluateAsync(learnerId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal static partial class SeedLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "The database already holds programmes, so seeding was skipped")]
    public static partial void AlreadySeeded(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Seeded: {Learners} learners, {Attendance} attendance marks, "
            + "{Badges} badges awarded by the real evaluator, "
            + "{Metrics} impact figures published by the real service")]
    public static partial void Seeded(ILogger logger, int learners, int attendance, int badges, int metrics);
}
