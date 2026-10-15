// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) .NET documentation.
//    - Microsoft (2026) Azure App Service and Azure SQL documentation.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Application.Listeners;
using TheeBuhle.Application.Safeguarding;
using TheeBuhle.Application.Security;
using TheeBuhle.Application.Services;
using TheeBuhle.Domain.Common;
using TheeBuhle.Domain.Events;
using TheeBuhle.Domain.Repositories;
using TheeBuhle.Infrastructure.Events;
using TheeBuhle.Infrastructure.Mail;
using TheeBuhle.Infrastructure.Notifications;
using TheeBuhle.Infrastructure.Payments;
using TheeBuhle.Infrastructure.Persistence;
using TheeBuhle.Infrastructure.Repositories;
using TheeBuhle.Infrastructure.Safeguarding;
using TheeBuhle.Infrastructure.Security;
using TheeBuhle.Infrastructure.Support;

namespace TheeBuhle.Infrastructure;

/// <summary>
/// Wires the Infrastructure layer to the Application layer's ports.
/// </summary>
/// <remarks>
/// <para>
/// This file is the only place the two layers meet. The Application layer
/// depends on <see cref="IUserRepository"/> and <see cref="IPasswordHasher"/>;
/// it has never heard of <c>UserRepository</c> or <c>Argon2PasswordHasher</c>.
/// That is what makes the dependency arrow point inward (Task 1 section
/// 9.3.12.2) and what lets every service be unit-tested with a fake.
/// </para>
/// <para>
/// It is also, deliberately, the one file to read to find out what the system is
/// actually built from.
/// </para>
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPersistence(configuration);
        services.AddRepositories();
        services.AddPorts(configuration);
        services.AddApplicationServices();
        services.AddDomainEventListeners();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TheeBuhle")
            ?? throw new InvalidOperationException(
                "No connection string named \"TheeBuhle\" is configured. "
                + "In production this is an Azure App Service connection setting; "
                + "locally, use `dotnet user-secrets`. It is never in appsettings.json.");

        services.AddDbContext<TheeBuhleDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                // Retry on transient faults. Not optional on Azure SQL: the
                // service moves databases between nodes for maintenance and
                // failover, which surfaces as a dropped connection for a few
                // seconds. Without this, a routine platform event is a burst of
                // 500s.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);

                sql.MigrationsAssembly(typeof(TheeBuhleDbContext).Assembly.FullName);

                // Longer than the default 30 seconds, because the impact refresh
                // aggregates across the attendance table. Still bounded, so a
                // pathological query fails rather than holding a connection.
                sql.CommandTimeout(60);
            }));

        // Scoped, so one request shares one context and therefore one change
        // tracker — which is what makes the Unit of Work's nesting work at all.
        services.AddScoped<IUnitOfWork, UnitOfWork>();
    }

    private static void AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPeopleRepository, PeopleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ILearnerRepository, LearnerRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IBadgeRepository, BadgeRepository>();
        services.AddScoped<IMentorshipRepository, MentorshipRepository>();
        services.AddScoped<ICommunicationRepository, CommunicationRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IDonationRepository, DonationRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IContentRepository, ContentRepository>();
        services.AddScoped<IEngagementRepository, EngagementRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IImpactRepository, ImpactRepository>();
        services.AddScoped<IPeerRepository, PeerRepository>();
        services.AddScoped<IBuddyPairingRepository, BuddyPairingRepository>();

        services.AddScoped<ILearningRepository, LearningRepository>();

        // The badge evaluator asks for the narrow two-method interface, and gets
        // the same instance. Interface segregation at the size of the need: the
        // badge tests satisfy IReadOnlyLearningProgress in a few lines instead of
        // standing up the whole learning hub.
        services.AddScoped<IReadOnlyLearningProgress>(provider =>
            provider.GetRequiredService<ILearningRepository>());
    }

    private static void AddPorts(this IServiceCollection services, IConfiguration configuration)
    {
        // Singletons: stateless, and the clock in particular must be one
        // instance so a test that freezes it freezes it everywhere.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IHtmlSanitiser, HtmlSanitiserAdapter>();

        services.AddSingleton(configuration.GetSection("Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("The Jwt configuration section is missing."));

        services.AddSingleton(configuration.GetSection("Signing").Get<SigningOptions>()
            ?? throw new InvalidOperationException("The Signing configuration section is missing."));

        services.AddSingleton(configuration.GetSection("Smtp").Get<SmtpOptions>() ?? new SmtpOptions());

        services.AddSingleton(configuration.GetSection("PayFast").Get<PayFastOptions>()
            ?? throw new InvalidOperationException("The PayFast configuration section is missing."));

        services.AddSingleton(configuration.GetSection("Section18A").Get<Section18AOptions>()
            ?? throw new InvalidOperationException("The Section18A configuration section is missing."));

        services.AddSingleton(configuration.GetSection("Engagement").Get<EngagementOptions>()
            ?? throw new InvalidOperationException("The Engagement configuration section is missing."));

        services.AddSingleton(configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions());

        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IUrlSigner, HmacUrlSigner>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IPaymentGateway, PayFastGateway>();

        services.AddHttpClient(nameof(PushChannel), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("accept-encoding", "gzip, deflate");
        });

        // -------------------------------------------------- the Strategy set
        //
        // Registered as four implementations of one interface, which is what
        // lets ChannelResolver receive IEnumerable<INotificationChannel> and
        // index them by their own Channel property. Adding WhatsApp later is one
        // class and one line here — no caller changes anywhere.
        services.AddScoped<INotificationChannel, InAppChannel>();
        services.AddScoped<INotificationChannel, EmailChannel>();
        services.AddScoped<INotificationChannel, SmsChannel>();
        services.AddScoped<INotificationChannel, PushChannel>();
        services.AddScoped<INotificationChannelResolver, ChannelResolver>();

        // ------------------------------------- the Chain of Responsibility set
        //
        // The three links and the chain that assembles them. Scoped rather than
        // singleton because SetNext mutates the links, so two concurrent
        // requests must not share them.
        services.AddScoped<MentorLeadHandler>();
        services.AddScoped<ProgrammeManagerHandler>();
        services.AddScoped<SafeguardingLeadHandler>();
        services.AddScoped<IEscalationChain, EscalationChain>();

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
    }

    private static void AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AccessPolicy>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<BadgeEvaluator>();
        services.AddScoped<NotificationService>();
        services.AddScoped<MentorshipService>();
        services.AddScoped<ConsentService>();
        services.AddScoped<AnnouncementService>();
        services.AddScoped<DonationService>();
        services.AddScoped<EventRegistrationService>();
        services.AddScoped<ImpactService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<PeerService>();

        // Development and test only in practice, but registered unconditionally
        // so the integration tests can resolve it without a second container.
        services.AddScoped<DatabaseSeeder>();
    }

    /// <summary>
    /// Registers the six Observer listeners.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatcher resolves <c>IDomainEventListener&lt;TEvent&gt;</c> from the
    /// container, so <strong>this list is the subscription list</strong> — and the
    /// registration order is the execution order, which matters for the
    /// attendance trio.
    /// </para>
    /// <para>
    /// The badge listener goes first, on purpose. It is the only one of the three
    /// that is not advisory, so if it is going to fail the transaction it should
    /// do so before two advisory listeners have queued notifications about work
    /// that is about to be rolled back.
    /// </para>
    /// </remarks>
    private static void AddDomainEventListeners(this IServiceCollection services)
    {
        // AttendanceRecorded — three consequences, one event.
        services.AddScoped<IDomainEventListener<AttendanceRecorded>, AwardBadgesOnAttendance>();
        services.AddScoped<IDomainEventListener<AttendanceRecorded>, NotifyParentOfAttendance>();
        services.AddScoped<IDomainEventListener<AttendanceRecorded>, RefreshLearnerSummary>();

        // ConcernFlagged — not advisory, so an unroutable concern rolls the
        // check-in back rather than saving a report that reached nobody.
        services.AddScoped<IDomainEventListener<ConcernFlagged>, EscalateFlaggedConcern>();

        services.AddScoped<IDomainEventListener<AnnouncementPublished>, FanOutAnnouncement>();

        services.AddScoped<IDomainEventListener<DonationCompleted>, IssueSection18ACertificate>();
    }
}
