using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Authorization;
using System.Net.Http.Headers;
using System.Text;
using WhatsappClient.Services;

var builder = WebApplication.CreateBuilder(args);

// Lee SIEMPRE de appsettings: Api:BaseUrl
var apiBase = (builder.Configuration["Api:BaseUrl"] ?? "https://nondeclaratory-brecken-unperpendicularly.ngrok-free.dev/").TrimEnd('/') + "/";
var empresaIdFallback = builder.Configuration["Api:EmpresaId"] ?? "1";

builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter());
});

// Cookie Auth (para la web del cliente)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.LogoutPath = "/Account/Logout";
        opt.AccessDeniedPath = "/Account/Denied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(2);
        opt.SlidingExpiration = true;
        opt.Cookie.Name = "whatsappclient.auth";
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opt.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();

// Session para guardar JWT y empresa_id
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = "whatsappclient.session";
    o.IdleTimeout = TimeSpan.FromHours(2);
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

// Handler para log de requests salientes
builder.Services.AddTransient<OutgoingDebugHandler>();

// HttpClient para ApiService
builder.Services
    .AddHttpClient<ApiService>(client =>
    {
        client.BaseAddress = new Uri(apiBase);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    })
    .AddHttpMessageHandler<OutgoingDebugHandler>()
    .AddTypedClient<ApiService>((http, sp) =>
    {
        var acc = sp.GetRequiredService<IHttpContextAccessor>();
        return new ApiService(http, empresaIdFallback, acc);
    });

// HttpClient named para AccountController (login)   usa el MISMO BaseUrl
builder.Services.AddHttpClient(nameof(WhatsappClient.Controllers.AccountController), client =>
{
    client.BaseAddress = new Uri(apiBase);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

// Session ANTES de Auth
app.UseSession();


app.Use(async (ctx, next) =>
{
    string? token = ctx.Session.GetString("JWT_TOKEN");
    if (!string.IsNullOrWhiteSpace(token))
    {
        token = token.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = token[7..].Trim();
        if (token.StartsWith("\"") && token.EndsWith("\""))
            token = token.Trim('"');

        var exp = JwtHelper.GetJwtExpiry(token);
        if (exp != null && DateTimeOffset.UtcNow >= exp.Value.AddSeconds(-60))
        {
            try { ctx.Session.Remove("JWT_TOKEN"); } catch { }
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (!ctx.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase))
            {
                var returnUrl = Uri.EscapeDataString(ctx.Request.Path + ctx.Request.QueryString);
                ctx.Response.Redirect("/Account/Login?returnUrl=" + returnUrl);
                return;
            }
        }
    }

    await next();
});

app.UseAuthentication();

// redirecci n ra z
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        var isAuth = context.User?.Identity?.IsAuthenticated == true;
        context.Response.Redirect(isAuth ? "/dashboard" : "/account/login");
        return;
    }
    await next();
});


app.UseAuthorization();

// Rutas
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static class JwtHelper
{
    public static DateTimeOffset? GetJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            static string B64UrlToB64(string s)
            {
                s = s.Replace('-', '+').Replace('_', '/');
                return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            }

            var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(B64UrlToB64(parts[1])));
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("exp", out var expEl))
            {
                long exp = expEl.ValueKind == System.Text.Json.JsonValueKind.String
                    ? long.Parse(expEl.GetString()!)
                    : expEl.GetInt64();
                return DateTimeOffset.FromUnixTimeSeconds(exp);
            }
        }
        catch { }
        return null;
    }
}
