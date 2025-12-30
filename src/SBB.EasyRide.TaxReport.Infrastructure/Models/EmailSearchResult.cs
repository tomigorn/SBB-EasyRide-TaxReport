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
    public string BodyContent { get; set; } = string.Empty;
    
    // Parsed values from email body
    public string? Amount { get; set; }
    public string? TransactionDate { get; set; }
}
