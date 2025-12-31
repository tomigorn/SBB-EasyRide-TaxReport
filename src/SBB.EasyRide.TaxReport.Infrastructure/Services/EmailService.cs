using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;
using System.Text.RegularExpressions;
using SBB.EasyRide.TaxReport.Infrastructure.Models;
using UglyToad.PdfPig;
using System.IO.Compression;
using iText.Html2pdf;

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

    public async Task<byte[]> GenerateMergedPdfReportAsync(string accessToken, List<EmailSearchResult> emails)
    {
        try
        {
            Console.WriteLine($"[EmailService] Starting report generation for {emails.Count} emails");
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            
            // Create ZIP archive to hold all HTML files and PDF attachments
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
            {
                int emailCounter = 1;
                
                foreach (var email in emails)
                {
                    try
                    {
                        Console.WriteLine($"[EmailService] Processing email {emailCounter}/{emails.Count}: {email.Subject}");
                        
                        // Fetch full email with HTML body
                        var emailHtml = await GetEmailHtmlBodyAsync(httpClient, email.Id);
                        
                        if (string.IsNullOrEmpty(emailHtml))
                        {
                            Console.WriteLine($"[EmailService] No HTML body for email: {email.Subject}");
                            emailCounter++;
                            continue;
                        }
                        
                        // Download and embed inline images
                        emailHtml = await EmbedInlineImagesAsync(httpClient, email.Id, emailHtml);
                        
                        // Wrap email HTML in a complete HTML document with print styles
                        var htmlDocument = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>{System.Net.WebUtility.HtmlEncode(email.Subject ?? "Email")}</title>
    <style>
        body {{ 
            font-family: Arial, sans-serif; 
            margin: 20px;
            background-color: white;
        }}
        .email-header {{ 
            border-bottom: 2px solid #333; 
            padding-bottom: 10px; 
            margin-bottom: 20px;
            page-break-after: avoid;
        }}
        .email-header h2 {{ 
            margin: 0 0 10px 0; 
            color: #333; 
        }}
        .email-meta {{ 
            font-size: 12px; 
            color: #666; 
            margin: 5px 0; 
        }}
        .email-body {{ 
            margin-top: 20px; 
        }}
        img {{ 
            max-width: 100%; 
            height: auto; 
        }}
        @media print {{
            body {{ margin: 0.5cm; }}
            .email-header {{ page-break-after: avoid; }}
        }}
    </style>
</head>
<body>
    <div class='email-header'>
        <h2>{System.Net.WebUtility.HtmlEncode(email.Subject ?? "No Subject")}</h2>
        <div class='email-meta'><strong>From:</strong> {System.Net.WebUtility.HtmlEncode(email.From ?? "Unknown")}</div>
        <div class='email-meta'><strong>Received:</strong> {email.ReceivedDateTime.ToLocalTime():dd.MM.yyyy HH:mm}</div>
        {(!string.IsNullOrEmpty(email.TransactionDate) ? $"<div class='email-meta'><strong>Transaction Date:</strong> {email.TransactionDate}</div>" : "")}
        {(!string.IsNullOrEmpty(email.Amount) ? $"<div class='email-meta'><strong>Amount:</strong> CHF {email.Amount}</div>" : "")}
    </div>
    <div class='email-body'>
        {emailHtml}
    </div>
</body>
</html>";
                        
                        // Create safe filename from subject
                        var safeSubject = string.Join("_", email.Subject?.Split(Path.GetInvalidFileNameChars()) ?? new[] { "Email" });
                        if (safeSubject.Length > 50)
                        {
                            safeSubject = safeSubject.Substring(0, 50);
                        }
                        
                        var emailFileName = $"{emailCounter:D3}_{safeSubject}.html";
                        
                        // Convert HTML to PDF using iText7
                        var pdfFileName = $"{emailCounter:D3}_{safeSubject}.pdf";
                        var pdfEntry = archive.CreateEntry(pdfFileName, CompressionLevel.Optimal);
                        using (var pdfStream = pdfEntry.Open())
                        {
                            HtmlConverter.ConvertToPdf(htmlDocument, pdfStream);
                        }
                        
                        Console.WriteLine($"[EmailService] Added email PDF: {pdfFileName}");
                        
                        // Get PDF attachments and add them to ZIP
                        var pdfAttachments = await GetPdfAttachmentsWithNamesAsync(httpClient, email.Id);
                        
                        if (pdfAttachments.Any())
                        {
                            Console.WriteLine($"[EmailService] Adding {pdfAttachments.Count} PDF attachment(s)");
                            
                            int attachCounter = 1;
                            foreach (var (pdfBytes, attachmentName) in pdfAttachments)
                            {
                                try
                                {
                                    var attachmentFileName = $"{emailCounter:D3}_{attachCounter:D2}_{attachmentName}";
                                    var attachmentEntry = archive.CreateEntry(attachmentFileName, CompressionLevel.Optimal);
                                    using (var entryStream = attachmentEntry.Open())
                                    {
                                        await entryStream.WriteAsync(pdfBytes, 0, pdfBytes.Length);
                                    }
                                    
                                    Console.WriteLine($"[EmailService] Added PDF attachment: {attachmentFileName}");
                                    attachCounter++;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[EmailService] Failed to add PDF attachment: {ex.Message}");
                                }
                            }
                        }
                        
                        emailCounter++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EmailService] Error processing email {email.Subject}: {ex.Message}");
                        Console.WriteLine($"[EmailService] Stack trace: {ex.StackTrace}");
                        emailCounter++;
                        // Continue with next email
                    }
                }
            }
            
            var result = zipStream.ToArray();
            Console.WriteLine($"[EmailService] ZIP generation complete. Size: {result.Length} bytes");
            
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] FATAL ERROR in PDF generation: {ex.Message}");
            Console.WriteLine($"[EmailService] Exception type: {ex.GetType().Name}");
            Console.WriteLine($"[EmailService] Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[EmailService] Inner exception: {ex.InnerException.Message}");
            }
            throw new Exception($"PDF generation failed: {ex.Message}", ex);
        }
    }
    
    private async Task<string?> GetEmailHtmlBodyAsync(HttpClient httpClient, string messageId)
    {
        try
        {
            var url = $"https://graph.microsoft.com/v1.0/me/messages/{messageId}?$select=body";
            var response = await httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EmailService] Failed to get email HTML: {response.StatusCode}");
                return null;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("body", out var bodyElement) &&
                bodyElement.TryGetProperty("content", out var contentElement))
            {
                return contentElement.GetString();
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] Error getting email HTML body: {ex.Message}");
            return null;
        }
    }
    
    private async Task<string> EmbedInlineImagesAsync(HttpClient httpClient, string messageId, string htmlContent)
    {
        try
        {
            // Find all CID references in HTML (src="cid:...")
            var cidMatches = Regex.Matches(htmlContent, @"src=""cid:([^""]+)""", RegexOptions.IgnoreCase);
            
            if (!cidMatches.Any())
            {
                return htmlContent;
            }
            
            // Get all attachments (including inline images)
            var url = $"https://graph.microsoft.com/v1.0/me/messages/{messageId}/attachments?$select=id,name,contentId,contentBytes,contentType";
            var response = await httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[EmailService] Failed to get attachments for inline images: {response.StatusCode}");
                return htmlContent;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            
            if (!doc.RootElement.TryGetProperty("value", out var attachments))
            {
                return htmlContent;
            }
            
            // Create a dictionary of contentId -> base64 data
            var imageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var attachment in attachments.EnumerateArray())
            {
                if (attachment.TryGetProperty("contentId", out var contentIdElement) &&
                    attachment.TryGetProperty("contentBytes", out var contentBytesElement) &&
                    attachment.TryGetProperty("contentType", out var contentTypeElement))
                {
                    var contentId = contentIdElement.GetString();
                    var contentBytes = contentBytesElement.GetString();
                    var contentType = contentTypeElement.GetString();
                    
                    if (!string.IsNullOrEmpty(contentId) && !string.IsNullOrEmpty(contentBytes))
                    {
                        // Remove angle brackets if present (e.g., <image001.png@01D...> -> image001.png@01D...)
                        contentId = contentId.Trim('<', '>');
                        imageMap[contentId] = $"data:{contentType};base64,{contentBytes}";
                    }
                }
            }
            
            // Replace all CID references with base64 data URIs
            foreach (Match match in cidMatches)
            {
                var cid = match.Groups[1].Value;
                if (imageMap.TryGetValue(cid, out var dataUri))
                {
                    htmlContent = htmlContent.Replace($"cid:{cid}", dataUri);
                    Console.WriteLine($"[EmailService] Embedded inline image: {cid}");
                }
            }
            
            return htmlContent;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] Error embedding inline images: {ex.Message}");
            return htmlContent; // Return original HTML if embedding fails
        }
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

    private async Task<List<(byte[] bytes, string name)>> GetPdfAttachmentsWithNamesAsync(HttpClient httpClient, string messageId)
    {
        var pdfList = new List<(byte[], string)>();
        
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
                
                if (!string.IsNullOrEmpty(attachment.ContentBytes) && !string.IsNullOrEmpty(attachment.Name))
                {
                    try
                    {
                        var pdfBytes = Convert.FromBase64String(attachment.ContentBytes);
                        pdfList.Add((pdfBytes, attachment.Name));
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
    
    private async Task<List<byte[]>> GetPdfAttachmentsAsync(HttpClient httpClient, string messageId)
    {
        var attachmentsWithNames = await GetPdfAttachmentsWithNamesAsync(httpClient, messageId);
        return attachmentsWithNames.Select(x => x.bytes).ToList();
    }
    
    private static string ExtractTextFromPdf(byte[] pdfBytes)
    {
        try
        {
            using var memoryStream = new MemoryStream(pdfBytes);
            using var document = UglyToad.PdfPig.PdfDocument.Open(memoryStream);
            
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
