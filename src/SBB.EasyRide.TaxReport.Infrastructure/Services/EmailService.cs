using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using SBB.EasyRide.TaxReport.Infrastructure.Models;

namespace SBB.EasyRide.TaxReport.Infrastructure.Services;

/// <summary>
/// Service for accessing Microsoft Graph emails using HttpClient
/// </summary>
public class EmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string?> GetLastEmailSubjectAsync(string accessToken)
    {
        // Debug: Log token info
        Console.WriteLine($"[EmailService] Token length: {accessToken?.Length ?? 0}");
        
        // Create HTTP client DIRECTLY with full URL (not using BaseAddress)
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        
        // Microsoft Graph DOES support personal accounts
        var fullUrl = "https://graph.microsoft.com/v1.0/me/messages?$select=subject&$top=1";
        Console.WriteLine($"[EmailService] Calling Microsoft Graph: {fullUrl}");
        
        var response = await httpClient.GetAsync(fullUrl);
        
        Console.WriteLine($"[EmailService] Response status: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[EmailService] Error response body: '{error}'");
            Console.WriteLine($"[EmailService] Error response length: {error?.Length ?? 0}");
            
            // Dump ALL response headers for debugging
            Console.WriteLine("[EmailService] === ALL RESPONSE HEADERS ===");
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"[EmailService]   {header.Key}: {string.Join(", ", header.Value)}");
            }
            foreach (var header in response.Content.Headers)
            {
                Console.WriteLine($"[EmailService]   {header.Key}: {string.Join(", ", header.Value)}");
            }
            Console.WriteLine("[EmailService] === END HEADERS ===");
            
            throw new HttpRequestException($"Graph API returned {response.StatusCode}: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GraphResponse>(json, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        return result?.Value?.FirstOrDefault()?.Subject;
    }

    public async Task<List<EmailSearchResult>> SearchEmailsAsync(string accessToken, DateTime startDate, DateTime endDate, string? subjectFilter = null)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Build OData $filter query
        var startDateStr = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endDateStr = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        
        var filter = $"receivedDateTime ge {startDateStr} and receivedDateTime le {endDateStr}";
        
        if (!string.IsNullOrWhiteSpace(subjectFilter))
        {
            filter += $" and contains(subject, '{subjectFilter.Replace("'", "''")}')";
        }

        var encodedFilter = HttpUtility.UrlEncode(filter);
        var fullUrl = $"https://graph.microsoft.com/v1.0/me/messages?$filter={encodedFilter}&$select=id,subject,receivedDateTime,from&$orderby=receivedDateTime desc&$top=100";
        
        Console.WriteLine($"[EmailService] Searching emails with filter: {filter}");

        var response = await httpClient.GetAsync(fullUrl);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Graph API returned {response.StatusCode}: {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<GraphSearchResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result?.Value?.Select(m => new EmailSearchResult
        {
            Id = m.Id ?? string.Empty,
            Subject = m.Subject ?? string.Empty,
            ReceivedDateTime = m.ReceivedDateTime,
            From = m.From?.EmailAddress?.Address ?? string.Empty
        }).ToList() ?? new List<EmailSearchResult>();
    }

    // Helper classes for JSON deserialization
    private class GraphResponse
    {
        public List<EmailMessage>? Value { get; set; }
    }

    private class EmailMessage
    {
        public string? Subject { get; set; }
    }

    private class GraphSearchResponse
    {
        public List<EmailMessageDetailed>? Value { get; set; }
    }

    private class EmailMessageDetailed
    {
        public string? Id { get; set; }
        public string? Subject { get; set; }
        public DateTime ReceivedDateTime { get; set; }
        public EmailFrom? From { get; set; }
    }

    private class EmailFrom
    {
        public EmailAddress? EmailAddress { get; set; }
    }

    private class EmailAddress
    {
        public string? Address { get; set; }
    }
}
