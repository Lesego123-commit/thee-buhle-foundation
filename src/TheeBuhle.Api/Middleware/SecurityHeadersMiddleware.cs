// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Microsoft (2026) ASP.NET Core documentation.
// =============================================================================

namespace TheeBuhle.Api.Middleware;

/// <summary>
/// Sets the response headers that constrain what a browser will do with this
/// site's pages.
/// </summary>
/// <remarks>
/// <para>
/// These are cheap and they close whole categories of attack rather than
/// individual holes, which is why they are worth setting even on an API that
/// mostly returns JSON. Each one below has a reason attached; a header nobody
/// can explain is a header somebody will eventually remove.
/// </para>
/// <para>
/// Set in middleware rather than in <c>web.config</c> or the Azure portal, so
/// they are in source control, they are the same locally and in production, and
/// they cannot be silently lost by a platform configuration change.
/// </para>
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    /// <summary>
    /// The content security policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second line of defence behind the allow-list sanitiser. If a stored
    /// XSS payload ever did reach a page, this is what stops it doing anything:
    /// no inline script, no external script, no framing.
    /// </para>
    /// <para>
    /// <c>script-src 'self'</c> with <strong>no</strong> <c>'unsafe-inline'</c>,
    /// which is the whole point — a policy with <c>'unsafe-inline'</c> permits
    /// exactly the injected <c>&lt;script&gt;</c> it is supposed to block. That
    /// constraint is why the front end has no inline handlers and why every
    /// script is a file under <c>wwwroot/assets/js</c>.
    /// </para>
    /// <para>
    /// <c>style-src</c> does allow <c>'unsafe-inline'</c>, which is a real and
    /// deliberate concession: the email templates and a handful of progress bars
    /// set widths inline, and CSS injection cannot execute code. Worth being
    /// honest about rather than claiming a perfect policy.
    /// </para>
    /// <para>
    /// <c>font-src</c> and the Google Fonts entry in <c>style-src</c> exist
    /// because the Heritage design system uses Cormorant Garamond.
    /// </para>
    /// </remarks>
    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self'; "
        + "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
        + "font-src 'self' https://fonts.gstatic.com; "
        + "img-src 'self' data:; "
        // The payment gateway's own domain, because the donation form posts to
        // it. Named explicitly rather than allowing all of https:.
        + "form-action 'self' https://www.payfast.co.za https://sandbox.payfast.co.za; "
        + "connect-src 'self'; "
        + "frame-ancestors 'none'; "
        + "base-uri 'self'; "
        + "object-src 'none'";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;

        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        // Stops a browser guessing that a .txt upload is really HTML and
        // rendering it — which is how a file upload becomes stored XSS.
        headers["X-Content-Type-Options"] = "nosniff";

        // Belt and braces with frame-ancestors above, for older browsers that
        // do not implement CSP framing.
        headers["X-Frame-Options"] = "DENY";

        // Send the full URL within this site and only the origin to anybody
        // else, so a learner's resource URL never leaks in a Referer header to
        // a third party.
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // This API needs none of these, so all three are refused. A permissions
        // policy is only meaningful if it denies things.
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        // X-Powered-By can be removed here because it is only ever added by a
        // reverse proxy or by IIS, i.e. before this runs.
        //
        // The `Server: Kestrel` header CANNOT. Kestrel writes it when the
        // response is flushed, which is after every middleware has finished —
        // so removing it here does nothing at all, as a curl -I against a
        // running instance shows. It is suppressed with
        // KestrelServerOptions.AddServerHeader = false in Program.cs instead.
        headers.Remove("X-Powered-By");

        if (!environment.IsDevelopment())
        {
            // HSTS, with preload. Deliberately not set in development, where
            // localhost is plain HTTP — a browser that has cached an HSTS policy
            // for localhost refuses to load any other project's dev server over
            // HTTP, and clearing it is genuinely awkward.
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        await next(context);
    }
}
