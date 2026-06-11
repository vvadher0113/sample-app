# Azure Setup Guide: Entra ID Authentication for SampleApp

## Overview

This guide walks through the Azure-side configuration required to enable authentication for **SampleApp**. The app supports two distinct sign-in flows:

| Flow | Provider | Who it's for | Sign-in endpoint |
|------|----------|--------------|-----------------|
| Internal | Microsoft Entra ID (your corporate tenant) | Employees / internal users | `/auth/signin/internal` |
| External | Microsoft Entra External ID (CIAM) | Customers / external users | `/auth/signin/external` |

Each flow requires its own **App Registration** and produces its own set of credentials that you will drop into App Service configuration.

---

## Prerequisites

- Global Administrator or Application Administrator role in your **corporate Entra ID tenant**
- Global Administrator role in your **Entra External ID tenant** (or permission to create app registrations there)
- Contributor access to the Azure App Service resource

---

## Part 1 — Internal Users (Corporate Entra ID)

Internal users are employees who sign in with their work accounts through your organisation's standard Microsoft Entra ID tenant.

### Step 1.1 — Create the App Registration

1. Go to the [Azure Portal](https://portal.azure.com) and open **Microsoft Entra ID** (your corporate tenant).
2. In the left menu, select **App registrations** → **New registration**.
3. Fill in the form:
   - **Name:** `SampleApp-Internal` (or your preferred name)
   - **Supported account types:** `Accounts in this organizational directory only (Single tenant)`
   - **Redirect URI:**
     - Platform: **Web**
     - URI: `https://<your-app-service-name>.azurewebsites.net/signin-oidc-internal`
4. Click **Register**.

> For local development, also add: `https://localhost:5001/signin-oidc-internal` as a second redirect URI (via **Authentication** → **Add URI**).

### Step 1.2 — Record the IDs

After registration, on the **Overview** page, copy and save:

| Value | Where to find it | Used as |
|-------|-----------------|---------|
| Application (client) ID | Overview page | `EntraInternal__ClientId` |
| Directory (tenant) ID | Overview page | `EntraInternal__TenantId` |

The **Instance** (login endpoint) for a standard Entra ID tenant is always:
```
https://login.microsoftonline.com/
```

### Step 1.3 — Create a Client Secret

1. In the app registration, go to **Certificates & secrets** → **Client secrets** → **New client secret**.
2. Set a **Description** (e.g., `SampleApp-Internal-Prod`) and choose an expiry.
3. Click **Add**.
4. **Copy the secret Value immediately** — it is only shown once.

> Store this secret in Azure Key Vault or directly in App Service Configuration. Never commit it to source control.

### Step 1.4 — Create App Roles

App roles drive the RBAC inside the application. The code checks for `App.Admin` and `App.Reader`.

1. In the app registration, go to **App roles** → **Create app role**.
2. Create the first role:
   - **Display name:** `App Admin`
   - **Allowed member types:** `Users/Groups`
   - **Value:** `App.Admin`
   - **Description:** Full administrative access
   - **Do you want to enable this app role?** Yes
3. Repeat for the second role:
   - **Display name:** `App Reader`
   - **Allowed member types:** `Users/Groups`
   - **Value:** `App.Reader`
   - **Description:** Read-only access
   - **Do you want to enable this app role?** Yes

### Step 1.5 — Assign Roles to Users

1. Go to **Enterprise applications** in Entra ID (same tenant).
2. Search for and open `SampleApp-Internal`.
3. Go to **Users and groups** → **Add user/group**.
4. Select a user, then select the role (`App.Admin` or `App.Reader`).
5. Click **Assign**.

> Users need to sign out and back in after role assignment for the new role to appear in their token.

---

## Part 2 — External Users (Entra External ID)

External users are customers or partners who sign in through a separate **Entra External ID** (CIAM) tenant. This is a different tenant from your corporate one.

### Step 2.1 — Create the App Registration (in External ID tenant)

1. In the Azure Portal, **switch to your Entra External ID tenant** using the directory switcher (top-right corner).
2. Open **Microsoft Entra ID** → **App registrations** → **New registration**.
3. Fill in the form:
   - **Name:** `SampleApp-ExternalId`
   - **Supported account types:** `Accounts in this organizational directory only`
   - **Redirect URI:**
     - Platform: **Web**
     - URI: `https://<your-app-service-name>.azurewebsites.net/signin-oidc`
4. Click **Register**.

> For local development, also add: `https://localhost:5001/signin-oidc`

### Step 2.2 — Record the IDs

| Value | Where to find it | Used as |
|-------|-----------------|---------|
| Application (client) ID | Overview page | `EntraExternalId__ClientId` |
| Directory (tenant) ID | Overview page | `EntraExternalId__TenantId` |

The **Instance** for Entra External ID uses your tenant subdomain:
```
https://<your-tenant-subdomain>.ciamlogin.com/
```

The **Domain** is your External ID tenant domain:
```
<your-tenant-domain>.onmicrosoft.com
```

Both values are visible on the **Overview** page of your Entra External ID tenant.

### Step 2.3 — Create a Client Secret

1. In the app registration, go to **Certificates & secrets** → **Client secrets** → **New client secret**.
2. Set a description and expiry, then click **Add**.
3. **Copy the secret Value immediately.**

### Step 2.4 — Create a User Flow (Sign Up / Sign In)

1. In the Entra External ID admin center, go to **User flows** → **New user flow**.
2. Select **Sign up and sign in**.
3. Give it a name, e.g., `susi`.
   - The full policy name will be `B2C_1_susi`.
4. Configure identity providers (Email, Google, Facebook, etc.) as needed.
5. Configure the user attributes you want to collect.
6. Click **Create**.
7. Note the **Policy name** — you will need it for `EntraExternalId__SignUpSignInPolicyId`.

### Step 2.5 — Create App Roles (External ID)

Repeat the same App Roles steps from Part 1 (Step 1.4) in this External ID app registration:
- `App.Admin` (Value: `App.Admin`)
- `App.Reader` (Value: `App.Reader`)

### Step 2.6 — Assign Roles to External Users

1. Go to **Enterprise applications** in the External ID tenant.
2. Open `SampleApp-ExternalId` → **Users and groups** → **Add user/group**.
3. Assign your test external users to the appropriate roles.

---

## Part 3 — App Service Configuration

All credentials are passed to the app via **App Service Application Settings**, which the .NET runtime reads as environment variables. The naming convention for nested config keys uses **double underscores** (`__`) instead of colons.

### Step 3.1 — Open App Service Configuration

1. Go to the Azure Portal → **App Services** → select your app.
2. In the left menu, select **Settings** → **Environment variables** (or **Configuration** → **Application settings** in older portal views).
3. You will add key-value pairs here.

### Step 3.2 — Add Internal Entra ID Settings

Click **+ Add** for each of the following:

| Application Setting Name | Value |
|--------------------------|-------|
| `EntraInternal__Instance` | `https://login.microsoftonline.com/` |
| `EntraInternal__TenantId` | _(Directory/tenant ID from Step 1.2)_ |
| `EntraInternal__ClientId` | _(Application/client ID from Step 1.2)_ |
| `EntraInternal__ClientSecret` | _(Secret value from Step 1.3)_ |
| `EntraInternal__CallbackPath` | `/signin-oidc-internal` |
| `EntraInternal__SignedOutCallbackPath` | `/signout-callback-oidc` |

### Step 3.3 — Add External Entra ID Settings

| Application Setting Name | Value |
|--------------------------|-------|
| `EntraExternalId__Instance` | `https://<your-tenant-subdomain>.ciamlogin.com/` |
| `EntraExternalId__Domain` | `<your-tenant-domain>.onmicrosoft.com` |
| `EntraExternalId__TenantId` | _(Directory/tenant ID from Step 2.2)_ |
| `EntraExternalId__ClientId` | _(Application/client ID from Step 2.2)_ |
| `EntraExternalId__ClientSecret` | _(Secret value from Step 2.3)_ |
| `EntraExternalId__SignUpSignInPolicyId` | _(Policy name from Step 2.4, e.g., `B2C_1_susi`)_ |
| `EntraExternalId__CallbackPath` | `/signin-oidc` |
| `EntraExternalId__SignedOutCallbackPath` | `/signout-callback-oidc` |

### Step 3.4 — Save and Restart

1. Click **Apply** (or **Save**) at the top of the Configuration page.
2. Confirm the restart prompt — App Service will restart with the new settings.

> **Tip:** Mark `EntraInternal__ClientSecret` and `EntraExternalId__ClientSecret` as **sensitive** by checking the "hidden value" option. This prevents them from being shown in plain text in the portal.

---

## Part 4 — Optional: Use Azure Key Vault Instead of Plain App Settings

If you prefer to keep secrets out of App Service configuration entirely, the app is already wired to load from Key Vault automatically.

### Step 4.1 — Enable Managed Identity on App Service

1. In the App Service, go to **Settings** → **Identity**.
2. Under **System assigned**, toggle **Status** to **On**.
3. Click **Save** and note the **Object (principal) ID**.

### Step 4.2 — Create Key Vault and Grant Access

1. Create an Azure Key Vault (or use an existing one).
2. Go to the Key Vault → **Access policies** → **Add Access Policy**.
3. Under **Secret permissions**, select: `Get`, `List`.
4. Under **Select principal**, search for your App Service name (the managed identity).
5. Click **Add** → **Save**.

### Step 4.3 — Add Secrets to Key Vault

In Key Vault → **Secrets** → **Generate/Import**, add secrets using **double-dash** notation (Key Vault doesn't support colons in names):

| Secret Name | Value |
|-------------|-------|
| `EntraInternal--TenantId` | _(tenant ID)_ |
| `EntraInternal--ClientId` | _(client ID)_ |
| `EntraInternal--ClientSecret` | _(secret value)_ |
| `EntraExternalId--TenantId` | _(tenant ID)_ |
| `EntraExternalId--ClientId` | _(client ID)_ |
| `EntraExternalId--ClientSecret` | _(secret value)_ |
| `EntraExternalId--SignUpSignInPolicyId` | _(policy name)_ |

### Step 4.4 — Point the App to Key Vault

In App Service **Environment variables**, add one setting:

| Application Setting Name | Value |
|--------------------------|-------|
| `KeyVault__Url` | `https://<your-keyvault-name>.vault.azure.net/` |

The app will load all secrets from Key Vault automatically. The non-secret settings (`Instance`, `Domain`, `CallbackPath`, etc.) can still be set as plain App Settings since they are not sensitive.

---

## Verification Checklist

After completing the setup, use this checklist to confirm everything is working:

- [ ] App Service restarts without errors (check **Log stream** or **Diagnose and solve problems**)
- [ ] Browsing to the app URL shows the landing page without a 500 error
- [ ] Clicking **Sign In (Internal)** redirects to `login.microsoftonline.com` and completes sign-in
- [ ] Clicking **Sign In (External)** redirects to `<subdomain>.ciamlogin.com` and completes sign-in
- [ ] After sign-in, `/api/me` returns user claims including the `auth_scheme` claim
- [ ] A user assigned `App.Admin` can access `/api/admin` (200 OK)
- [ ] A user without `App.Admin` gets 403 on `/api/admin`

---

## Troubleshooting

| Error | Likely cause | Fix |
|-------|-------------|-----|
| `AADSTS50011` — redirect URI mismatch | The callback URI in the App Registration does not exactly match the app's URL | Add the exact URI (including path) to the App Registration → Authentication → Redirect URIs |
| 500 on startup | A required config value is missing or a placeholder `<...>` was not replaced | Check App Service Log stream; verify all 8 internal and 9 external settings are present |
| Roles missing from `/api/me` | User was assigned a role after their last sign-in | Sign out and sign in again to get a fresh token |
| 403 on `/api/admin` despite having the role | Role value has a typo | Confirm the role **Value** in App Registration → App roles is exactly `App.Admin` (case-sensitive) |
| External sign-in fails with policy error | Wrong `SignUpSignInPolicyId` value | Check the policy name in Entra External ID → User flows (format: `B2C_1_<name>`) |

---

## Summary of Values to Collect

Use this as a quick checklist while working through Parts 1 and 2:

**Internal (Part 1):**
- [ ] Tenant ID
- [ ] Client ID
- [ ] Client Secret
- Instance is always `https://login.microsoftonline.com/`

**External (Part 2):**
- [ ] Tenant subdomain (for Instance URL)
- [ ] Tenant domain (`.onmicrosoft.com`)
- [ ] Tenant ID
- [ ] Client ID
- [ ] Client Secret
- [ ] User flow / policy name
