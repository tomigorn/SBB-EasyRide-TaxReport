namespace SBB.EasyRide.TaxReport.Infrastructure.Services;

/// <summary>
/// Service for accessing Microsoft Graph emails
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gets the subject of the most recent email
    /// </summary>
    /// <param name="accessToken">Microsoft Graph access token</param>
    /// <returns>Subject of the last email, or null if none found</returns>
    Task<string?> GetLastEmailSubjectAsync(string accessToken);
}
