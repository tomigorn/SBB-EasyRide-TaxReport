namespace SBB.EasyRide.TaxReport.Infrastructure.Models;

/// <summary>
/// Represents a single email from search results
/// </summary>
public class EmailSearchResult
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime ReceivedDateTime { get; set; }
    public string From { get; set; } = string.Empty;
}
