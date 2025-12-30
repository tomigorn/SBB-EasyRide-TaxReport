# Azure Authentication Setup Instructions

## Step 1: Update Azure App Registration

Your app is currently configured as a **Single Page Application (SPA)**. You need to change it to **Web** for server-side authentication.

### In Azure Portal:

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Select your app registration
4. Go to **Authentication** in the left menu

### Update Platform Configuration:

1. **Remove** the existing **Single-page application** platform
2. Click **Add a platform**
3. Select **Web**
4. Add these Redirect URIs:
   - `https://localhost:7022/signin-oidc`
   - `http://localhost:5198/signin-oidc`
5. Add these Logout URLs:
   - `https://localhost:7022/signout-callback-oidc`
   - `http://localhost:5198/signout-callback-oidc`
6. Under **Implicit grant and hybrid flows**, check:
   - ☑ **ID tokens** (used for hybrid flows)
7. Click **Save**

### Create Client Secret:

1. Go to **Certificates & secrets** in the left menu
2. Click **New client secret**
3. Add a description (e.g., "Dev Secret")
4. Choose an expiration period
5. Click **Add**
6. **IMPORTANT:** Copy the **Value** immediately (you won't be able to see it again!)

## Step 2: Get Your Configuration Values

From the **Overview** page of your App Registration, copy:

1. **Application (client) ID** - This is your Client ID
2. **Directory (tenant) ID** - This is your Tenant ID

## Step 3: Set User Secrets

Open a terminal in the Web project folder and run these commands (replace with your actual values):

```powershell
cd "C:\Users\Gamer\Documents\development\SBB-EasyRide-TaxReport\src\SBB.EasyRide.TaxReport.Web"

dotnet user-secrets set "AzureAd:TenantId" "YOUR-TENANT-ID-HERE"
dotnet user-secrets set "AzureAd:ClientId" "YOUR-CLIENT-ID-HERE"
dotnet user-secrets set "AzureAd:ClientSecret" "YOUR-CLIENT-SECRET-HERE"
```

### Example:
```powershell
dotnet user-secrets set "AzureAd:TenantId" "12345678-1234-1234-1234-123456789012"
dotnet user-secrets set "AzureAd:ClientId" "abcdefgh-abcd-abcd-abcd-abcdefghijkl"
dotnet user-secrets set "AzureAd:ClientSecret" "abc~123-def456ghi789"
```

## Step 4: Verify API Permissions

Make sure these Microsoft Graph permissions are still configured:

1. Go to **API permissions** in your App Registration
2. Verify you have:
   - `Mail.Read` (Delegated)
   - `Mail.ReadBasic` (Delegated)
   - `User.Read` (Delegated)
3. If any are missing, click **Add a permission** → **Microsoft Graph** → **Delegated permissions**
4. Click **Grant admin consent** (if you have admin rights, or ask an admin)

## Step 5: Run Your Application

```powershell
cd "C:\Users\Gamer\Documents\development\SBB-EasyRide-TaxReport\src\SBB.EasyRide.TaxReport.Web"
dotnet run
```

Navigate to `https://localhost:7022` and click the **Login** button!

## What Happens After Login:

1. User clicks **Login** → redirected to Microsoft login page
2. User enters credentials and consents to permissions
3. User is redirected back to your app
4. The app receives:
   - **ID Token** (user identity info)
   - **Access Token** (to call Microsoft Graph API)
5. The **Logout** button becomes enabled
6. User's name is displayed

## Accessing Microsoft Graph from Your Backend:

In your backend API, you can call Microsoft Graph using the access token. The token is automatically managed by `Microsoft.Identity.Web`.

Example of calling Graph API in a Blazor component:

```csharp
@inject Microsoft.Graph.GraphServiceClient GraphClient

private async Task GetUserEmails()
{
    var messages = await GraphClient.Me.Messages
        .Request()
        .Top(10)
        .GetAsync();
    
    // Process messages...
}
```

## Security Notes:

- ✅ User secrets are stored in: `%APPDATA%\Microsoft\UserSecrets\76c987b2-9502-4516-9950-b2340d2ebf83\secrets.json`
- ✅ This file is NOT in your git repository
- ✅ Never commit secrets to source control
- ✅ For production, use Azure Key Vault or environment variables

## Troubleshooting:

### Error: "AADSTS50011: The redirect URI does not match"
- Make sure you added the exact redirect URIs in Azure
- Check that you're using the correct port (7022 for HTTPS)

### Error: "IDW10104: Both client secret and client certificate cannot be null"
- You forgot to set the ClientSecret in user secrets

### Login button doesn't redirect:
- Check that `app.MapRazorPages()` is in Program.cs
- Make sure `app.UseAuthentication()` comes before `app.UseAuthorization()`
