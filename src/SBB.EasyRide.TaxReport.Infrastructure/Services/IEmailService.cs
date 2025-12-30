using SBB.EasyRide.TaxReport.Infrastructure.Models;

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
    
    /// <summary>
    /// Searches for emails within a date range and matching subject filters
    /// </summary>
    Task<List<EmailSearchResult>> SearchEmailsAsync(string accessToken, DateTime startDate, DateTime endDate, List<string>? subjectFilters = null);
}
