using SBB.EasyRide.TaxReport.Web.Components;
using SBB.EasyRide.TaxReport.Web.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using SBB.EasyRide.TaxReport.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for Docker deployment
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080); // HTTP
    options.ListenAnyIP(8081, listenOptions =>
    {
        // HTTPS will be configured via environment variables in Docker
        // ASPNETCORE_Kestrel__Certificates__Default__Path and Password
        if (builder.Environment.IsProduction())
        {
            listenOptions.UseHttps();
        }
    });
});

// Add Microsoft Identity authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.GetSection("AzureAd").Bind(options);
        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                // Force account selection every time
                context.ProtocolMessage.Prompt = "select_account";
                
                // Request Mail.Read scope upfront during initial login
                if (!context.ProtocolMessage.Scope.Contains("Mail.Read"))
                {
                    context.ProtocolMessage.Scope += " https://graph.microsoft.com/Mail.Read";
                }
                
                return Task.CompletedTask;
            }
        };
    })
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddMicrosoftGraph(builder.Configuration.GetSection("MicrosoftGraph"))
    .AddInMemoryTokenCaches();

// Configure authentication cookies to not persist
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15); // Short session
    options.SlidingExpiration = false; // Don't extend session
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthorization();

// Add controllers and Razor Pages support for Microsoft Identity UI
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register Infrastructure services
builder.Services.AddHttpClient(); // Required for EmailService
builder.Services.AddScoped<IEmailService, EmailService>();

// Register startup service to track server restarts
builder.Services.AddSingleton<StartupService>();

var app = builder.Build();

// Note: In-memory token cache automatically clears on server restart
// Users will need to login fresh each time the app starts

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // Removed HSTS for Docker deployment (handle HTTPS at reverse proxy level)
}

// Only redirect to HTTPS in development (local)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map controllers and Razor Pages for authentication endpoints
app.MapControllers();
app.MapRazorPages();

app.Run();
