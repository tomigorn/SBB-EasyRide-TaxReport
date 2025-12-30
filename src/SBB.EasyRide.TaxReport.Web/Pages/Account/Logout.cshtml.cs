using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class LogoutModel : PageModel
{
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(ILogger<LogoutModel> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userName = User.Identity?.Name ?? "Unknown";
        _logger.LogInformation("User {UserName} is logging out", userName);

        // Sign out from the local cookie authentication
        // This also clears the in-memory token cache associated with this session
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        
        _logger.LogInformation("User {UserName} logged out successfully. Session and tokens cleared.", userName);

        return Redirect("/");
    }
}
