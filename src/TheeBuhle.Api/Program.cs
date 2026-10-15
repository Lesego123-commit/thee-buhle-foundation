// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
//    - Microsoft (2026) Azure App Service and Azure SQL documentation.
// =============================================================================

using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TheeBuhle.Api.Middleware;
using TheeBuhle.Application.Abstractions;
using TheeBuhle.Infrastructure;
using TheeBuhle.Infrastructure.Persistence;
using TheeBuhle.Infrastructure.Security;

// =============================================================================
//  Thee Buhle Foundation — API host.
//
//  The composition root. Everything the application is made of is registered
//  here or in Infrastructure's AddInfrastructure, and the pipeline below is the
//  order every request passes through.
//
//  The middleware ORDER is not arbitrary and is the thing most worth reading
//  carefully — several of the entries only work because of what runs before
//  them. Each one says so at its call site.
// =============================================================================

// One-off maintenance commands, handled before the web host is built: they
// need the database and nothing else, so they must not require the JWT signing
// key or any other production setting.
if (args is [TheeBuhle.Api.AdminBootstrap.Command, ..])
{
    Environment.ExitCode = await TheeBuhle.Api.AdminBootstrap.RunAsync(args[1..]);

    return;
}

if (args is [TheeBuhle.Api.DemoDataCommand.Command])
{
    Environment.ExitCode = await TheeBuhle.Api.DemoDataCommand.RunAsync();

    return;
}

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
//  Configuration
// -----------------------------------------------------------------------------

// User-secrets in development only. The JWT signing key, the SMTP password and
// the PayFast passphrase are real credentials for a real organisation, so they
// are never in appsettings.json — in production they are Azure App Service
// application settings, which arrive through the environment provider that
// CreateBuilder already added.
//
// Environment variables are then re-added, so they sit ABOVE user-secrets in
// precedence. .NET resolves configuration by last-source-wins, and CreateBuilder
// adds the environment provider before this point — so without the second
// AddEnvironmentVariables, a value in user-secrets would silently beat one set
// in the environment.
//
// That ordering is backwards from what everyone expects, and it is not
// theoretical: the integration tests inject their own database name and were
// quietly ignored, so the whole suite ran against a developer's local database
// instead of a disposable one. An environment variable should always be the
// thing that wins locally.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
    builder.Configuration.AddEnvironmentVariables();
}

// -----------------------------------------------------------------------------
//  Services
// -----------------------------------------------------------------------------

// Suppresses `Server: Kestrel` on every response.
//
// It has to be done here rather than in the security-headers middleware, and
// that is worth recording because the middleware version looks like it works.
// Kestrel writes this header when the response is flushed — after all
// middleware has run — so `headers.Remove("Server")` removes a header that has
// not been added yet and the response still carries it.
//
// Not a vulnerability on its own. But there is no reason to tell a scanner
// which server and version to look up known issues for.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserAccessor>();

// The HTTP-backed request context replaces Infrastructure's system one, so the
// audit trail records the real client address rather than "system". Registered
// after AddInfrastructure so this registration wins.
builder.Services.AddScoped<IRequestContext, HttpRequestContext>();

// Note: the general rate limit is applied as a *global* limiter in
// AddRateLimiting rather than as a controller filter, so it covers the health
// check and the static files too — and so a new controller arrives protected
// rather than unlimited until somebody remembers an attribute.
// Razor Pages for the twelve public website pages.
//
// Not static HTML, and the reason is the shell. Twelve static files would each
// carry their own copy of the header, the navigation, the newsletter block and
// the footer — so changing a footer link is twelve edits and eleven chances to
// miss one. A layout is one file.
//
// It also lets the page metadata and the next-event announcement bar be
// rendered SERVER-SIDE. On a slow Vaal mobile connection a client-rendered
// announcement bar is visibly empty for a second and then jumps, and a
// client-rendered <title> is invisible to a search crawler and to a WhatsApp
// link preview — which is how most people will actually encounter this site.
builder.Services.AddRazorPages();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        // Enums as their names, matching how they are stored. A client reading
        // "Concerned" rather than "3" is one fewer mapping table to keep in
        // sync, and a reordered enum cannot silently change an API response.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Every validation message this API sends is written for the person who
        // filled in the form, and the request shapes carry their own wording for
        // that. This catches the one case they cannot reach: a body the binder
        // could not read at all — a missing required member, or malformed JSON —
        // which the framework reports against the parameter's own name as
        // "The request field is required."
        //
        // A donor who saw that would be told their submission failed by a
        // sentence about a variable in our code.
        var builtIn = options.InvalidModelStateResponseFactory;

        options.InvalidModelStateResponseFactory = context =>
        {
            Humanise(context.ModelState);

            return builtIn(context);
        };
    });

// Rewrites binder-level complaints, leaving every attribute's own message
// untouched. Kept beside its registration rather than in a helper file, because
// it exists only to serve the line above it.
//
// It also re-keys them. The binder reports a bad value against "$.topic" and
// against the parameter name "request", neither of which is the name of a form
// control — so the website received a message it could not attach to anything
// and showed it only in the summary. Re-keyed as "topic", it lands on the field
// it is about.
static void Humanise(ModelStateDictionary modelState)
{
    var rewritten = new List<(string Key, string Message)>();
    var generic = new List<string>();

    foreach (var (key, entry) in modelState.ToList())
    {
        var isBinderComplaint = entry?.Errors.Any(error =>
            error.ErrorMessage.Contains("field is required", StringComparison.OrdinalIgnoreCase)
            || error.ErrorMessage.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
            || error.ErrorMessage.Contains("is invalid", StringComparison.OrdinalIgnoreCase)
            || error.ErrorMessage.Contains("JSON", StringComparison.OrdinalIgnoreCase)) == true;

        if (!isBinderComplaint)
        {
            continue;
        }

        modelState.ClearValidationState(key);
        modelState.Remove(key);

        var field = FieldName(key);

        if (field is null)
        {
            generic.Add(key);

            continue;
        }

        rewritten.Add((field, $"Please check the {Spaced(field)} — we could not read what was sent."));
    }

    foreach (var (key, message) in rewritten)
    {
        modelState.TryAddModelError(key, message);
    }

    // The whole-body complaint is only worth saying when no specific field was
    // named. Said alongside one, it is the same failure reported twice — and
    // the binder raises it under two keys of its own ("$" and the parameter's
    // name), so only the first is kept.
    if (rewritten.Count == 0 && generic.Count > 0)
    {
        modelState.TryAddModelError(
            generic[0],
            "We could not read that submission. Please fill the form in again.");
    }
}

// "$.partySize" is the field partySize; "request" and "$" are the body itself,
// which is not a field anybody filled in.
static string? FieldName(string key) => key switch
{
    "request" or "$" or "" => null,
    _ when key.StartsWith("$.", StringComparison.Ordinal) => key[2..],
    _ => key,
};

// "partySize" reads as "party size" to somebody who never saw the field name.
static string Spaced(string field) => string.Concat(field.Select(character =>
    char.IsUpper(character) ? " " + char.ToLowerInvariant(character) : character.ToString()))
    .Replace(".", " ", StringComparison.Ordinal)
    .Trim();

builder.Services.AddRateLimiting();

// -----------------------------------------------------------------------------
//  Authentication
// -----------------------------------------------------------------------------

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

// Validated at startup, so a signing key that is too short stops the deployment
// rather than failing on the first login attempt in production.
jwt.Validate();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // All four validations on. The issuer and audience checks are the
            // ones people disable "temporarily": without them, a token minted by
            // any other service that happens to share the signing key would be
            // accepted here.
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),

            // Thirty seconds, not the default five minutes. The access token
            // lives fifteen minutes, so a five-minute grace period extends it by
            // a third — which quietly undoes the reason for choosing fifteen.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization();

// -----------------------------------------------------------------------------
//  OpenAPI
// -----------------------------------------------------------------------------

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Thee Buhle Foundation API",
        Version = "v1",
        Description =
            "The API behind the Thee Buhle Foundation website, admin console and mobile app.\n\n"
            + "Two things are worth knowing before reading the endpoints:\n\n"
            + "- **Authorisation is data-dependent, not just role-dependent.** A role check "
            + "decides whether a kind of user may reach an endpoint; whether *this* parent may "
            + "read *that* learner is resolved against the guardian-link table on every request.\n"
            + "- **Flagged safeguarding concerns are not in this API for most callers.** They are "
            + "filtered in the database query rather than removed from the response, so a "
            + "parent's request never loads one.",
        Contact = new OpenApiContact
        {
            Name = "Thee Buhle Foundation",
            Url = new Uri("https://theebuhlefoundation.co.za"),
        },
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "A 15-minute access token from POST /api/auth/sign-in.",
    });

    // A reference to the scheme defined above rather than a second inline
    // copy of it, which is what the OpenAPI spec intends and what stops the two
    // definitions drifting apart.
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", null, null)] = [],
    });

    options.IncludeXmlComments(
        Path.Combine(AppContext.BaseDirectory, "TheeBuhle.Api.xml"),
        includeControllerXmlComments: true);
});

// -----------------------------------------------------------------------------
//  CORS
// -----------------------------------------------------------------------------

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

    // Named origins only. AllowAnyOrigin cannot be combined with credentials
    // anyway, and an API that serves a known website and a known app has no
    // reason to answer a page it has never heard of.
    policy.WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TheeBuhleDbContext>("database");

var app = builder.Build();

// =============================================================================
//  The pipeline.
//
//  Order matters, and these six are in this sequence for specific reasons:
// =============================================================================

// 1. Exceptions first, so it wraps everything after it. A handler registered
//    later cannot catch what an earlier component throws.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 2. Security headers early, so they are set even on a response produced by
//    something that fails later — including a 500 and a 429.
app.UseMiddleware<SecurityHeadersMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Re-execute a 404 through the site's own not-found page, keeping both the
// status code and the URL the visitor typed. A redirect would answer 302 and
// then 200, which tells a crawler the mistyped URL is a real page that moved.
//
// Scoped away from /api on purpose: an API 404 must stay JSON, because its
// caller is parsing JSON and an HTML error page is a parse failure rather than
// a readable message.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found"));

app.UseStaticFiles();

// Routing explicitly, and here rather than by default.
//
// WebApplication inserts UseRouting at the very front of the pipeline when
// nobody asks for it, which would put it BEFORE the status-code branch above.
// That branch clears the matched endpoint and re-runs the pipeline from its own
// position, so with routing behind it nothing would re-match the new path and a
// mistyped URL answered 404 with an empty body — correct status, blank page.
app.UseRouting();

app.UseCors();

// 3. Authentication, then 4. the principal rebuild — in that order, because the
//    rebuild reads the subject claim that authentication has just validated.
app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();

app.UseAuthorization();

// 5. Rate limiting AFTER the principal is known, so the limiter can partition
//    per user rather than per address. Thirty parents on one venue's Wi-Fi share
//    an address, and an address-keyed limiter would have them throttling each
//    other.
app.UseRateLimiter();

app.MapControllers();

app.MapRazorPages();

// Two checks, for two different callers.
//
// /health runs the database check. The deploy pipeline gates on it, because a
// passing check means the new code started AND can reach its schema.
//
// /health/live runs no checks: it answers 200 if the process is serving
// requests. App Service's own health check polls it every minute. Pointing
// that poll at /health instead would wake the serverless database every
// minute, so it never auto-pauses, and the free offer's monthly allowance
// would be gone in about three days. After that the database pauses until the
// next month and the whole site goes down.
app.MapHealthChecks("/health");

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Thee Buhle Foundation API v1");
        options.DocumentTitle = "Thee Buhle Foundation API";
    });
}

// Anything that is not an API route, a page, a static file or the health check
// answers 404 — which the status-code-pages branch above turns into the site's
// own not-found page, so a mistyped URL lands somewhere with navigation on it
// instead of at a dead end.
//
// Scoped to non-API paths: an unknown /api/ route should answer 404 as JSON,
// because its caller is a client parsing JSON and an HTML error page would be a
// parse failure rather than a readable message.
app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;

        return context.Response.WriteAsJsonAsync(new
        {
            status = 404,
            title = "not_found",
            detail = "No such endpoint. See /swagger for the ones that exist.",
        });
    }

    // Nothing written here: the status-code-pages branch above re-executes
    // this request through /not-found and writes the page itself.
    context.Response.StatusCode = StatusCodes.Status404NotFound;

    return Task.CompletedTask;
});

// -----------------------------------------------------------------------------
//  Migrations
// -----------------------------------------------------------------------------
//
//  Applied at startup in development only. In production they run as a
//  deliberate step in the deployment workflow, before the new code goes live —
//  because a migration applied by whichever instance happens to boot first is a
//  schema change with no review, no backup checkpoint and a race between
//  instances if the App Service scales out.
//
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();

    await scope.ServiceProvider
        .GetRequiredService<TheeBuhleDbContext>()
        .Database
        .MigrateAsync();

    // Idempotent: it checks whether the programmes exist and returns if they
    // do, so restarting the app does not accumulate a second copy of
    // everything. Development only — production data comes from the client.
    await scope.ServiceProvider
        .GetRequiredService<DatabaseSeeder>()
        .SeedAsync();
}

await app.RunAsync();

/// <summary>
/// Named so the integration tests can reference this assembly through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
