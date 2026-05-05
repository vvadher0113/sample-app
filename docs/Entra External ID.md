# Entra External ID + MSAL + RBAC + Conditional Access Runbook

## Purpose
This runbook configures the SampleApp for:
- Sign in with Microsoft Entra External ID
- Role-based authorization (RBAC) in the app
- Conditional Access policy enforcement for the web app

Use this document as your repeatable setup guide for dev/test/prod.

## What is already implemented in code
The app code is already updated to:
- Use OpenID Connect with Microsoft Identity Web (MSAL-backed)
- Require authenticated users by default
- Expose role-protected endpoint `/api/admin` requiring role `App.Admin`
- Expose identity inspection endpoint `/api/me`
- Add browser test actions in the homepage for Sign In, Sign Out, and role checks

## Prerequisites
- Access to your Entra External ID tenant
- Permission to create app registrations and enterprise app assignments
- Azure App Service for deployment
- .NET 8 SDK installed locally
- Azure CLI logged in (for deployment-related steps)

## 1) Register the web app in Entra External ID
1. Open Microsoft Entra admin center for your External ID tenant.
2. Go to App registrations -> New registration.
3. Name: `SampleApp-ExternalId-Web` (or your preferred name).
4. Supported account types: choose the option matching your tenant design.
5. Configure Redirect URI (Web):
   - `https://localhost:5001/signin-oidc`
   - `https://<your-app-name>.azurewebsites.net/signin-oidc`
6. After create, record:
   - Application (client) ID
   - Directory (tenant) ID
7. Go to Certificates & secrets -> New client secret.
8. Copy secret value immediately and store it securely.

## 2) Configure user flow / policy
1. In External ID, create or select a Sign up and sign in user flow.
2. Note the policy name, for example `B2C_1_susi`.
3. Confirm this policy is enabled and assigned to the app journey.

## 3) Create app roles for RBAC
1. Open App registrations -> your app -> App roles.
2. Create role:
   - Display name: `App Admin`
   - Value: `App.Admin`
   - Allowed member types: Users/Groups
   - Enabled: Yes
3. Create role:
   - Display name: `App Reader`
   - Value: `App.Reader`
   - Allowed member types: Users/Groups
   - Enabled: Yes
4. Save and wait a few minutes for propagation.

## 4) Assign roles to users/groups
1. Open Enterprise applications -> your app -> Users and groups.
2. Assign at least one test user to `App.Admin`.
3. Assign at least one test user to `App.Reader`.
4. Keep one unassigned user for negative testing.

## 5) Configure application settings
Do not keep secrets in source control.

### Local development (recommended via User Secrets)
From repository root:

```powershell
cd SampleApp
dotnet user-secrets init
dotnet user-secrets set "EntraExternalId:Instance" "https://<your-tenant-subdomain>.ciamlogin.com/"
dotnet user-secrets set "EntraExternalId:Domain" "<your-tenant-domain>.onmicrosoft.com"
dotnet user-secrets set "EntraExternalId:TenantId" "<tenant-guid>"
dotnet user-secrets set "EntraExternalId:ClientId" "<app-client-id>"
dotnet user-secrets set "EntraExternalId:ClientSecret" "<client-secret>"
dotnet user-secrets set "EntraExternalId:SignUpSignInPolicyId" "<policy-name>"
dotnet user-secrets set "EntraExternalId:CallbackPath" "/signin-oidc"
dotnet user-secrets set "EntraExternalId:SignedOutCallbackPath" "/signout-callback-oidc"
```

### Azure App Service configuration
Set these app settings in App Service -> Configuration:
- `EntraExternalId__Instance`
- `EntraExternalId__Domain`
- `EntraExternalId__TenantId`
- `EntraExternalId__ClientId`
- `EntraExternalId__ClientSecret`
- `EntraExternalId__SignUpSignInPolicyId`
- `EntraExternalId__CallbackPath`
- `EntraExternalId__SignedOutCallbackPath`

## 6) Validate authentication and authorization
1. Run locally:

```powershell
dotnet run --project SampleApp/SampleApp.csproj
```

2. Open `https://localhost:5001`.
3. Click `Sign In`.
4. Click `Check /api/me` and confirm claims and roles are returned.
5. Click `Check /api/admin`.

Expected behavior:
- User with `App.Admin` -> 200 OK
- User without `App.Admin` -> 403 Forbidden
- Unauthenticated user -> redirected/challenged to sign in

## 7) Configure Conditional Access for this web app
1. Go to Entra admin center -> Protection/Security -> Conditional Access -> New policy.
2. Name policy, for example: `CA-SampleApp-RequireMFA`.
3. Assignments:
   - Users: select test users/group first
   - Target resources (Cloud apps): select this app's enterprise application
4. Conditions (optional):
   - Locations, device platforms, sign-in risk, etc.
5. Grant controls:
   - Require multi-factor authentication
   - Optionally require compliant device
6. Enable in `Report-only` first.
7. Test sign in and inspect logs.
8. Switch policy to `On` after validation.

## 8) Deployment checklist
- Redirect URIs include both local and App Service URLs
- Client secret is set only in secure settings
- App roles created and assigned
- User flow/policy name is correct
- Conditional Access tested in report-only
- Production sign-in test done with real user accounts

## 9) Troubleshooting
### AADSTS50011 (redirect URI mismatch)
- Ensure `/signin-oidc` and exact host are present in App registration redirect URIs.

### No roles in token
- Verify app roles are defined in app registration and assigned in enterprise app.
- Sign out and sign in again to refresh token.

### 403 on `/api/admin` for admin user
- Confirm role value is exactly `App.Admin`.
- Verify role claim appears in `/api/me` response.

### Conditional Access not applying
- Confirm policy scope includes the correct cloud app and test user.
- Check sign-in logs and CA policy results.

## 10) Security recommendations
- Move secret management to Key Vault with managed identity for production.
- Rotate client secrets regularly.
- Keep CA policies in report-only before enabling in production.
- Use separate app registrations for non-production and production environments.

## Change log
- 2026-05-05: Initial runbook created for first-time Entra External ID + MSAL + RBAC + CA setup.
