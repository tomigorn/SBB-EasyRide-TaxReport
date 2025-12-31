# SBB & ZVV EasyRide Tax Report Generator

A sophisticated .NET 9.0 Blazor Server application that automates Swiss public transport tax reporting by intelligently extracting transaction data from SBB and ZVV email receipts.

![.NET](https://img.shields.io/badge/.NET-9.0-purple)
![Blazor](https://img.shields.io/badge/Blazor-Server-blue)
![Azure AD](https://img.shields.io/badge/Azure-AD-0078D4)
![Microsoft Graph](https://img.shields.io/badge/Microsoft-Graph%20API-7FBA00)

## 🎯 Overview

This application solves a real-world problem: generating accurate tax reports from Swiss public transport receipts (SBB/ZVV) scattered across email inboxes. It demonstrates advanced email processing, intelligent data extraction, PDF manipulation, and secure OAuth authentication with personal Microsoft accounts.

**Key Capabilities:**
- 🔐 Secure authentication via Azure AD with personal Microsoft accounts
- 📧 Smart email search with flexible filtering (OR logic across multiple subject patterns)
- 💰 Intelligent amount extraction from 6+ different Swiss currency formats
- 📄 PDF text extraction and parsing from email attachments
- 📊 Clean CSV export with proper numeric formatting
- 🖨️ Individual PDF generation with embedded images in ZIP archives
- ⚡ Client secret expiration monitoring with guided renewal instructions

## 🏗️ Architecture

The solution follows clean architecture principles with clear separation of concerns:

```
SBB-EasyRide-TaxReport/
├── src/
│   ├── SBB.EasyRide.TaxReport.Web/          # Blazor Server presentation layer
│   │   ├── Components/
│   │   │   └── Pages/
│   │   │       └── Home.razor                 # Main UI with interactive search
│   │   ├── Services/
│   │   │   └── StartupService.cs              # Session management
│   │   └── Program.cs                          # App configuration & DI
│   │
│   └── SBB.EasyRide.TaxReport.Infrastructure/ # Business logic & external integrations
│       ├── Services/
│       │   └── EmailService.cs                 # Microsoft Graph API integration
│       └── Models/
│           └── EmailSearchResult.cs            # Domain model
```

### Technology Stack

**Frontend:**
- Blazor Server (Interactive Server mode, prerender: false)
- Bootstrap 5 for responsive UI
- JavaScript interop for file downloads

**Backend:**
- .NET 9.0
- Microsoft.Identity.Web 4.2.0 (Azure AD authentication)
- Microsoft Graph API v1.0 (Mail.Read scope)

**PDF Processing:**
- iText7 9.4.0 (PDF creation)
- itext7.pdfhtml 6.3.0 (HTML to PDF conversion)
- itext.bouncy-castle-adapter (cryptography)
- UglyToad.PdfPig 1.7.0 (PDF text extraction)

**Storage:**
- In-memory token caching (suitable for development)
- User Secrets for secure credential storage

## 🧠 Technical Challenges & Solutions

### Challenge 1: Personal Microsoft Account Authentication

**Problem:** Azure AD apps default to work/school accounts. Personal accounts require specific tenant configuration.

**Solution:**
```csharp
"TenantId": "common"  // Allows both personal and work accounts
```

Implemented custom `OnRedirectToIdentityProvider` event to:
- Force account selection on every login (`Prompt = "select_account"`)
- Request `Mail.Read` scope upfront during authentication
- Prevent stale authentication after server restarts

### Challenge 2: Multi-Format Amount Extraction

**Problem:** Swiss currency amounts appear in 6+ different formats:
- `CHF 42.50`
- `42.50 CHF`
- `CHF 2'206.70` (Swiss thousand separator)
- `2'206.70 CHF`
- `42,50 CHF` (comma as decimal)
- PDF-embedded amounts in various positions

**Solution:** Implemented cascading regex strategies in `EmailService.ExtractAmount()`:
```csharp
private string? ExtractAmount(string text)
{
    var patterns = new[]
    {
        @"CHF\s*([\d']+[.,]\d{2})",           // CHF 2'206.70
        @"([\d']+[.,]\d{2})\s*CHF",           // 2'206.70 CHF
        @"Total.*?([\d']+[.,]\d{2})",         // Total: 42.50
        @"Betrag.*?([\d']+[.,]\d{2})",        // Betrag: 42.50
        @"Amount.*?([\d']+[.,]\d{2})",        // Amount: 42.50
        @"([\d']+[.,]\d{2})"                   // Fallback: any decimal
    };
    
    // Try each pattern until successful extraction
    // Normalize Swiss format (replace ' with nothing, , with .)
}
```

### Challenge 3: PDF Attachment Processing

**Problem:** Transaction amounts often only appear in PDF attachments, not email body.

**Solution:** Multi-stage PDF processing pipeline:
1. Detect PDF attachments via Microsoft Graph API
2. Download PDF bytes from `$value` endpoint
3. Extract text using PdfPig's `PdfDocument.GetPages()`
4. Apply same amount extraction strategies as email body
5. Track PDF source for debugging

### Challenge 4: HTML Email to PDF Conversion

**Problem:** Emails contain complex HTML with inline images (CID references) that break in PDF.

**Solution:** Implemented `EmbedInlineImagesAsync()`:
```csharp
// Find all <img src="cid:..."> references
// Download attachments with matching contentId via Graph API
// Convert to base64 data URIs: data:image/png;base64,...
// Replace CID references with embedded images
// Convert HTML to PDF using iText7.pdfhtml
```

Each email generates a separate PDF in a ZIP archive with proper metadata and formatting.

### Challenge 5: Configuration Validation & User Guidance

**Problem:** Users encounter cryptic OAuth errors when secrets are missing or expired.

**Solution:** Proactive configuration validation with contextual help:
- Checks for missing `ClientId`, `ClientSecret`, `TenantId` on startup
- Parses `ClientSecretExpiration` with `InvariantCulture` (handles MM/DD/YYYY Azure format)
- Displays targeted error messages with step-by-step Azure Portal instructions
- Shows exact PowerShell commands for fixing issues
- Includes `dotnet user-secrets list` for verification

### Challenge 6: CSV Number Formatting

**Problem:** Swiss apostrophe separator (`2'206.70`) corrupted in CSV, displaying as `2â€™206.70` (UTF-8 encoding issue).

**Solution:** Strip all non-numeric characters except decimal point:
```csharp
var amount = new string(email.Amount
    .Where(c => char.IsDigit(c) || c == '.')
    .ToArray());
```

## 🚀 Setup & Installation

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Azure AD application registration
- Microsoft account with emails

### 1. Azure AD Application Setup

1. **Create App Registration:**
   - Go to [Azure Portal](https://portal.azure.com) → Azure Active Directory → App registrations
   - Click "New registration"
   - Name: `SBB EasyRide Tax Report`
   - Supported account types: **Accounts in any organizational directory and personal Microsoft accounts**
   - Redirect URI: `Web` - `https://localhost:7022/signin-oidc`
   - Click "Register"

2. **Configure Authentication:**
   - In app registration, go to "Authentication"
   - Add another redirect URI: `https://localhost:7022/signout-callback-oidc`
   - Enable "ID tokens" under Implicit grant
   - Save changes

3. **Create Client Secret:**
   - Go to "Certificates & secrets" → "Client secrets"
   - Click "New client secret"
   - Description: `Dev Secret`
   - Expires: 24 months (recommended)
   - Click "Add"
   - **⚠️ Copy the Value immediately** (you won't see it again!)
   - Note the "Expires" date (format: MM/DD/YYYY)

4. **Copy Configuration Values:**
   - Overview page: Copy **Application (client) ID**
   - You already have: **Client secret Value** and **Expires date**
   - Tenant ID: Use `common` (for personal accounts)

### 2. Local Project Setup

1. **Clone Repository:**
   ```bash
   git clone https://github.com/yourusername/SBB-EasyRide-TaxReport.git
   cd SBB-EasyRide-TaxReport
   ```

2. **Configure User Secrets:**
   ```bash
   cd src/SBB.EasyRide.TaxReport.Web
   
   dotnet user-secrets set "AzureAd:ClientId" "YOUR_CLIENT_ID"
   dotnet user-secrets set "AzureAd:ClientSecret" "YOUR_CLIENT_SECRET_VALUE"
   dotnet user-secrets set "AzureAd:TenantId" "common"
   dotnet user-secrets set "AzureAd:ClientSecretExpiration" "12/30/2027"
   ```

   Replace:
   - `YOUR_CLIENT_ID` with Application (client) ID from Azure
   - `YOUR_CLIENT_SECRET_VALUE` with the secret Value you copied
   - `12/30/2027` with your actual expiration date (MM/DD/YYYY format)

3. **Verify Secrets:**
   ```bash
   dotnet user-secrets list
   ```

   Should output:
   ```
   AzureAd:ClientId = ecf88f21-...
   AzureAd:ClientSecret = GtY8Q~...
   AzureAd:TenantId = common
   AzureAd:ClientSecretExpiration = 12/30/2027
   ```

4. **Run Application:**
   ```bash
   dotnet run
   ```

   Navigate to: <a href="https://localhost:7022" target="_blank">https://localhost:7022</a>

## 📖 Usage

1. **Login:**
   - Click "Login" button
   - Select your Microsoft account
   - Grant Mail.Read permission
   - Your email appears in bold after authentication

2. **Configure Search:**
   - Select date mode: Year (default) or specific date range
   - Add subject filters (OR logic):
     - Quick add: Click "Add common filters" button
     - Manual: Type custom filter and click "Add"
   - Example filters:
     - `Ihr Abo-Kauf im ZVV-Ticketshop`
     - `Ihr Online-Kauf bei der SBB`
     - `EasyRide Kaufquittung`

3. **Search Emails:**
   - Click "Search Emails"
   - View results table with:
     - Received Date
     - Transaction Date (extracted)
     - From address
     - Subject
     - Amount (CHF) - right-aligned
   - Total automatically calculated

4. **Generate Reports:**
   - **CSV:** Clean numeric format, ready for tax software
   - **PDF:** ZIP archive with separate PDFs for each email + attachments

## 🔐 Security Considerations

- **User Secrets:** Never commit secrets to source control
- **Token Caching:** In-memory (non-persistent for development)
- **Cookie Security:** `SecurePolicy.Always`, HttpOnly, 15-minute expiration
- **OAuth Scopes:** Minimal permissions (`Mail.Read` only)
- **Secret Expiration:** Proactive monitoring with 2-year rotation

## 🛠️ Development Notes

### Key Design Decisions

1. **Blazor Server over WASM:** Better for secure OAuth flows, no client-side secret exposure
2. **Interactive Server Mode:** Real-time updates during email search
3. **Prerender: False:** Prevents authentication state issues on refresh
4. **Separate PDFs:** Avoids merge complexity, preserves original formatting
5. **ZIP over Merged PDF:** Simpler, faster, maintains individual email context

### Known Limitations

- **Email Limit:** 75 emails per search (Microsoft Graph default `$top`)
- **Cache:** In-memory only (clears on restart, suitable for single-user dev)
- **No Pagination:** For >75 emails, use narrower date filters

### Enhancement Ideas

- [ ] Add pagination for large result sets
- [ ] Implement distributed cache (Redis) for production
- [ ] Add email body preview in results table
- [ ] Save/load filter presets
- [ ] Export to Excel format
- [ ] Date range validation (end >= start)
- [ ] Unit tests for amount extraction
- [ ] Application Insights integration

## 📝 License

This work is licensed under a [Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License](https://creativecommons.org/licenses/by-nc-nd/4.0/).

[![CC BY-NC-ND 4.0](https://licensebuttons.net/l/by-nc-nd/4.0/88x31.png)](https://creativecommons.org/licenses/by-nc-nd/4.0/)

**You are free to:**
- ✅ View and use the code for personal, non-commercial purposes

**Under the following terms:**
- **Attribution** — You must give appropriate credit
- **NonCommercial** — You may not use the material for commercial purposes
- **NoDerivatives** — If you remix, transform, or build upon the material, you may not distribute the modified material

For commercial licensing inquiries, please contact the author.

## 🙏 Acknowledgments

- Swiss Federal Railways (SBB) and Zürcher Verkehrsverbund (ZVV) for inspiring the need
- Microsoft Graph API for robust email access
- iText7 for powerful PDF manipulation
- UglyToad.PdfPig for reliable PDF text extraction

## 📧 Contact

For questions or issues, please open a GitHub issue.

---

**Made with ☕ and ❤️ for Swiss tax season**