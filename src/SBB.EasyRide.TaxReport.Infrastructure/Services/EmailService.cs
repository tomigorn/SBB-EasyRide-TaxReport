using System.Net.Http.Headers;
using System.Text.Json;

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

    // Helper classes for JSON deserialization
    private class GraphResponse
    {
        public List<EmailMessage>? Value { get; set; }
    }

    private class EmailMessage
    {
        public string? Subject { get; set; }
    }
}
