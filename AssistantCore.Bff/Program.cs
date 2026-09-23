using AssistantCore.Bff.Configuration;
using AssistantCore.Bff.Proxy;
using Azure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Identity.Web;

const string BffApiPolicy = "BffApi";

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsEnvironment("LocalLive"))
{
    builder.Configuration.AddUserSecrets<Program>();
}

var publicOrigin = builder.Configuration["Bff:PublicOrigin"];
if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var publicOriginUri)
    || publicOriginUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("Bff:PublicOrigin must be an absolute HTTPS URL.");
}

var dataProtectionBlobUri = builder.Configuration["DataProtectionKeyStorage:BlobStorageUri"];
var dataProtectionKeyUri = builder.Configuration["DataProtectionKeyStorage:KeyVaultKeyUri"];
var hasDataProtectionBlob = !string.IsNullOrWhiteSpace(dataProtectionBlobUri);
var hasDataProtectionKey = !string.IsNullOrWhiteSpace(dataProtectionKeyUri);

if (hasDataProtectionBlob != hasDataProtectionKey)
{
    throw new InvalidOperationException(
        "DataProtectionKeyStorage:BlobStorageUri and DataProtectionKeyStorage:KeyVaultKeyUri must be configured together.");
}

if (builder.Environment.IsEnvironment("Certif") && !hasDataProtectionBlob)
{
    throw new InvalidOperationException("Persistent Data Protection key storage is required in Certif.");
}

var dataProtection = builder.Services
    .AddDataProtection()
    .SetApplicationName("AssistantCore.Bff");

if (hasDataProtectionBlob)
{
    var credential = new DefaultAzureCredential();
    dataProtection
        .PersistKeysToAzureBlobStorage(new Uri(dataProtectionBlobUri!), credential)
        .ProtectKeysWithAzureKeyVault(new Uri(dataProtectionKeyUri!), credential);
}

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

builder.Services.Configure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.Cookie.Name = "__Host-onpremia-session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(BffApiPolicy, policy =>
    {
        policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-onpremia-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});

builder.Services
    .AddOptions<DownstreamApiOptions>()
    .Bind(builder.Configuration.GetSection(DownstreamApiOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "DownstreamApi:BaseUrl must be an absolute URL.")
    .Validate(options => options.Scopes.Length > 0 && options.Scopes.All(scope => !string.IsNullOrWhiteSpace(scope)), "DownstreamApi:Scopes must contain at least one scope.")
    .ValidateOnStart();

builder.Services.AddHttpClient(nameof(DownstreamApiProxy), client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddScoped<DownstreamApiProxy>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Request.Scheme = publicOriginUri.Scheme;
    context.Request.Host = new HostString(publicOriginUri.Authority);

    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    if (context.Request.Path.StartsWithSegments("/bff")
        || context.Request.Path.StartsWithSegments("/signin-oidc")
        || context.Request.Path.StartsWithSegments("/signout-callback-oidc"))
    {
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
    }

    await next();
});

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/bff/login", (string? returnUrl) =>
{
    var redirectUri = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
    return Results.Challenge(
        new AuthenticationProperties { RedirectUri = redirectUri },
        [OpenIdConnectDefaults.AuthenticationScheme]);
});

app.MapGet("/bff/session", (HttpContext context) =>
    Results.Ok(new
    {
        isAuthenticated = context.User.Identity?.IsAuthenticated is true,
        name = context.User.Identity?.Name
    }))
    .AllowAnonymous();

app.MapGet("/bff/csrf", (IAntiforgery antiforgery, HttpContext context) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
})
    .RequireAuthorization(BffApiPolicy);

app.MapPost("/bff/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(context);
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { signOutUrl = "/bff/signout" });
})
    .RequireAuthorization(BffApiPolicy);

app.MapGet("/bff/signout", () =>
    Results.SignOut(
        new AuthenticationProperties { RedirectUri = "/" },
        [OpenIdConnectDefaults.AuthenticationScheme]))
    .AllowAnonymous();

app.MapMethods(
    "/api/{**path}",
    ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"],
    async (HttpContext context, string path, DownstreamApiProxy proxy, CancellationToken cancellationToken) =>
    {
        await proxy.ProxyAsync(context, path, cancellationToken);
    })
    .RequireAuthorization(BffApiPolicy);

app.Run();

static bool IsLocalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return false;
    }

    return returnUrl.StartsWith("/", StringComparison.Ordinal)
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);
}

public partial class Program;
