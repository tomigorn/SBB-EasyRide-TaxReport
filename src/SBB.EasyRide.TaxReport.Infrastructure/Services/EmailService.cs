using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using System.Text.RegularExpressions;
using SBB.EasyRide.TaxReport.Infrastructure.Models;
using UglyToad.PdfPig;

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

    public async Task<List<EmailSearchResult>> SearchEmailsAsync(string accessToken, DateTime startDate, DateTime endDate, List<string>? subjectFilters = null)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        // Build OData $filter query
        var startDateStr = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endDateStr = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        
        var filter = $"receivedDateTime ge {startDateStr} and receivedDateTime le {endDateStr}";
        
        // Add subject filters with OR logic
        if (subjectFilters != null && subjectFilters.Any())
        {
            var subjectConditions = subjectFilters
                .Select(s => $"contains(subject, '{s.Replace("'", "''")}')")
                .ToList();
            
            filter += $" and ({string.Join(" or ", subjectConditions)})";
        }

        var encodedFilter = HttpUtility.UrlEncode(filter);
        var fullUrl = $"https://graph.microsoft.com/v1.0/me/messages?$filter={encodedFilter}&$select=id,subject,receivedDateTime,from,body&$orderby=receivedDateTime desc&$top=100";
        
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

        var results = new List<EmailSearchResult>();
        
        foreach (var m in result?.Value ?? new List<EmailMessageDetailed>())
        {
            var bodyContent = m.Body?.Content ?? string.Empty;
            
            // Debug: Log first 500 chars of email body
            if (bodyContent.Length > 0)
            {
                Console.WriteLine($"[EmailService] Email body preview (first 500 chars): {bodyContent.Substring(0, Math.Min(500, bodyContent.Length))}");
            }
            
            var amount = ExtractAmount(bodyContent);
            var transactionDate = ExtractTransactionDate(bodyContent);
            
            // If amount not found in body, check PDF attachments
            if (string.IsNullOrEmpty(amount))
            {
                Console.WriteLine($"[EmailService] Amount not found in email body, checking attachments for email: {m.Subject}");
                var pdfData = await GetPdfAttachmentsAsync(httpClient, m.Id ?? string.Empty);
                
                if (pdfData.Any())
                {
                    Console.WriteLine($"[EmailService] Found {pdfData.Count} PDF attachment(s), extracting text...");
                    foreach (var pdf in pdfData)
                    {
                        var pdfText = ExtractTextFromPdf(pdf);
                        Console.WriteLine($"[EmailService] PDF text length: {pdfText.Length}");
                        
                        // Debug: Print first 500 characters of PDF text
                        if (pdfText.Length > 0)
                        {
                            Console.WriteLine($"[EmailService] PDF text preview (first 500 chars): {pdfText.Substring(0, Math.Min(500, pdfText.Length))}");
                        }
                        
                        amount = ExtractAmount(pdfText);
                        if (!string.IsNullOrEmpty(amount))
                        {
                            Console.WriteLine($"[EmailService] Found amount in PDF: {amount}");
                            // Also try to get date from PDF if not already found
                            if (string.IsNullOrEmpty(transactionDate))
                            {
                                transactionDate = ExtractTransactionDate(pdfText);
                            }
                            break;
                        }
                    }
                }
            }
            
            results.Add(new EmailSearchResult
            {
                Id = m.Id ?? string.Empty,
                Subject = m.Subject ?? string.Empty,
                ReceivedDateTime = m.ReceivedDateTime,
                From = m.From?.EmailAddress?.Address ?? string.Empty,
                BodyContent = bodyContent,
                Amount = amount,
                TransactionDate = transactionDate
            });
        }
        
        return results;
    }

    private static string? ExtractAmount(string emailBody)
    {
        // Strip HTML tags and decode HTML entities
        var textContent = Regex.Replace(emailBody, @"<[^>]+>", " ");
        textContent = System.Net.WebUtility.HtmlDecode(textContent);
        
        // Replace all whitespace variations with simple spaces
        textContent = Regex.Replace(textContent, @"\s+", " ");
        
        Console.WriteLine($"[EmailService] Extracting amount from cleaned text length: {textContent.Length}");
        
        // Debug: Print the text around "Total" if it exists
        var totalIndex = textContent.IndexOf("Total", StringComparison.OrdinalIgnoreCase);
        if (totalIndex >= 0)
        {
            var start = Math.Max(0, totalIndex - 50);
            var length = Math.Min(150, textContent.Length - start);
            var context = textContent.Substring(start, length);
            Console.WriteLine($"[EmailService] Found 'Total' in text, context: ...{context}...");
        }
        
        // Strategy 1: Look for "Betrag" followed by CHF amount (may be on next line)
        var match = Regex.Match(textContent, @"Betrag\s+CHF\s+([\d.,']+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            Console.WriteLine($"[EmailService] Found amount via 'Betrag CHF': {match.Groups[1].Value}");
            return match.Groups[1].Value.Trim();
        }
        
        // Strategy 2: Look for "Total" followed by amount (ZVV format: "Total 809.00")
        // Use \s* to allow zero or more spaces, and make pattern more flexible
        match = Regex.Match(textContent, @"Total\s*([\d]+[.,']?[\d]*[.,']?[\d]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var amount = match.Groups[1].Value.Trim();
            Console.WriteLine($"[EmailService] Found amount via 'Total': {amount}");
            // Normalize: convert comma to period for decimal parsing
            amount = amount.Replace("'", "").Replace(",", ".");
            return amount;
        }
        
        // Strategy 3: Look for "Total" with CHF
        match = Regex.Match(textContent, @"Total\s+CHF\s+([\d.,']+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            Console.WriteLine($"[EmailService] Found amount via 'Total CHF': {match.Groups[1].Value}");
            return match.Groups[1].Value.Trim();
        }
        
        // Strategy 4: Look for Swiss "Fr." format (ZVV uses "Summe in Fr.")
        match = Regex.Match(textContent, @"(?:Summe|Total)\s+in\s+Fr\.\s+([\d.,']+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            Console.WriteLine($"[EmailService] Found amount via 'Summe in Fr.': {match.Groups[1].Value}");
            return match.Groups[1].Value.Trim();
        }
        
        // Strategy 5: Look for any "CHF <amount>" pattern
        var allMatches = Regex.Matches(textContent, @"CHF\s+([\d.,']+)", RegexOptions.IgnoreCase);
        Console.WriteLine($"[EmailService] Found {allMatches.Count} CHF amounts in text");
        
        if (allMatches.Count > 0)
        {
            decimal maxAmount = 0;
            string? maxAmountStr = null;
            
            foreach (Match m in allMatches)
            {
                var amountStr = m.Groups[1].Value.Trim();
                Console.WriteLine($"[EmailService] Checking CHF amount: '{amountStr}'");
                var cleanAmount = amountStr.Replace("'", "").Replace(",", ".");
                if (decimal.TryParse(cleanAmount, out var amount))
                {
                    Console.WriteLine($"[EmailService] Parsed as: {amount}");
                    if (amount > maxAmount)
                    {
                        maxAmount = amount;
                        maxAmountStr = amountStr;
                    }
                }
            }
            
            if (maxAmountStr != null)
            {
                Console.WriteLine($"[EmailService] Found amount via CHF pattern (largest): {maxAmountStr}");
                return maxAmountStr;
            }
        }
        
        // Strategy 6: Look for "Fr. <amount>" pattern (Swiss format)
        allMatches = Regex.Matches(textContent, @"Fr\.\s+([\d.,']+)", RegexOptions.IgnoreCase);
        Console.WriteLine($"[EmailService] Found {allMatches.Count} Fr. amounts in text");
        
        if (allMatches.Count > 0)
        {
            decimal maxAmount = 0;
            string? maxAmountStr = null;
            
            foreach (Match m in allMatches)
            {
                var amountStr = m.Groups[1].Value.Trim();
                Console.WriteLine($"[EmailService] Checking Fr. amount: '{amountStr}'");
                var cleanAmount = amountStr.Replace("'", "").Replace(",", ".");
                if (decimal.TryParse(cleanAmount, out var amount))
                {
                    Console.WriteLine($"[EmailService] Parsed as: {amount}");
                    if (amount > maxAmount)
                    {
                        maxAmount = amount;
                        maxAmountStr = amountStr;
                    }
                }
            }
            
            if (maxAmountStr != null)
            {
                Console.WriteLine($"[EmailService] Found amount via Fr. pattern (largest): {maxAmountStr}");
                return maxAmountStr;
            }
        }
        
        Console.WriteLine("[EmailService] No amount found in text");
        return null;
    }

    private async Task<List<byte[]>> GetPdfAttachmentsAsync(HttpClient httpClient, string messageId)
    {
        var pdfList = new List<byte[]>();
        
        try
        {
            // Get attachments list
            var attachmentsUrl = $"https://graph.microsoft.com/v1.0/me/messages/{messageId}/attachments";
            var response = await httpClient.GetAsync(attachmentsUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EmailService] Failed to get attachments: {response.StatusCode}");
                return pdfList;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var attachmentsResult = JsonSerializer.Deserialize<AttachmentsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            // Filter PDF attachments
            var pdfAttachments = attachmentsResult?.Value?.
                Where(a => a.Name?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true)
                .ToList() ?? new List<Attachment>();
            
            Console.WriteLine($"[EmailService] Found {pdfAttachments.Count} PDF attachment(s)");
            
            foreach (var attachment in pdfAttachments)
            {
                Console.WriteLine($"[EmailService] Processing PDF: {attachment.Name}");
                
                if (!string.IsNullOrEmpty(attachment.ContentBytes))
                {
                    try
                    {
                        var pdfBytes = Convert.FromBase64String(attachment.ContentBytes);
                        pdfList.Add(pdfBytes);
                        Console.WriteLine($"[EmailService] Successfully decoded PDF: {attachment.Name} ({pdfBytes.Length} bytes)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EmailService] Failed to decode PDF {attachment.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] Error getting PDF attachments: {ex.Message}");
        }
        
        return pdfList;
    }
    
    private static string ExtractTextFromPdf(byte[] pdfBytes)
    {
        try
        {
            using var memoryStream = new MemoryStream(pdfBytes);
            using var document = PdfDocument.Open(memoryStream);
            
            var text = string.Join(" ", document.GetPages().Select(page => page.Text));
            Console.WriteLine($"[EmailService] Extracted {text.Length} characters from PDF");
            
            return text;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] Failed to extract text from PDF: {ex.Message}");
            return string.Empty;
        }
    }

    private static string? ExtractTransactionDate(string emailBody)
    {
        // Strip HTML tags if present
        var textContent = Regex.Replace(emailBody, @"<[^>]+>", " ");
        
        // Strategy 1: Look for "Datum" followed by date on same or next line
        var match = Regex.Match(textContent, @"Datum[\s\r\n:]+(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (match.Success)
        {
            Console.WriteLine($"[EmailService] Found date via 'Datum': {match.Groups[1].Value}");
            return match.Groups[1].Value.Trim();
        }
        
        // Strategy 2: Look for "Kaufdatum:" followed by date
        match = Regex.Match(textContent, @"Kaufdatum:[\s\r\n]+(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (match.Success)
        {
            Console.WriteLine($"[EmailService] Found date via 'Kaufdatum': {match.Groups[1].Value}");
            return match.Groups[1].Value.Trim();
        }
        
        // Strategy 3: Look for any date in DD.MM.YYYY format (take the first one)
        match = Regex.Match(textContent, @"\b(\d{2}\.\d{2}\.\d{4})\b");
        if (match.Success)
        {
            Console.WriteLine($"[EmailService] Found date via generic pattern: {match.Groups[1].Value}");
            return match.Groups[1].Value.Trim();
        }
        
        Console.WriteLine("[EmailService] No date found in email");
        return null;
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
        public EmailBody? Body { get; set; }
    }

    private class EmailBody
    {
        public string? Content { get; set; }
        public string? ContentType { get; set; }
    }

    private class EmailFrom
    {
        public EmailAddress? EmailAddress { get; set; }
    }

    private class EmailAddress
    {
        public string? Address { get; set; }
    }
    
    private class AttachmentsResponse
    {
        public List<Attachment>? Value { get; set; }
    }
    
    private class Attachment
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? ContentType { get; set; }
        public string? ContentBytes { get; set; }
    }
}
