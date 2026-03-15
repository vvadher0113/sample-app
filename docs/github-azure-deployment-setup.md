# GitHub Actions → Azure App Service Deployment Setup Guide

> Step-by-step guide to configure GitHub Actions with OIDC (federated credentials) for deploying to Azure App Service — no passwords or keys stored.

---

## Prerequisites

- Azure CLI installed (`az` command available)
- An Azure subscription with an App Service created
- A GitHub repository with your code pushed
- Owner/Contributor access on the Azure subscription

---

## Step 1: Create an App Registration in Azure AD (Entra ID)

```bash
az ad app create --display-name "github-deploy-<your-app-name>"
```

Note the **Application (client) ID** from the output — you'll need it in later steps.

Example output:
```
"appId": "ae254290-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
```

---

## Step 2: Create a Service Principal

The App Registration alone isn't enough — you need a Service Principal for role assignments.

```bash
az ad sp create --id <APPLICATION_CLIENT_ID>
```

Example:
```bash
az ad sp create --id ae254290-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

---

## Step 3: Assign Contributor Role

Grant the Service Principal **Contributor** access on the resource group where your App Service lives.

```bash
az role assignment create \
  --assignee <APPLICATION_CLIENT_ID> \
  --role "Contributor" \
  --scope /subscriptions/<SUBSCRIPTION_ID>/resourceGroups/<RESOURCE_GROUP_NAME>
```

Example:
```bash
az role assignment create \
  --assignee ae254290-xxxx-xxxx-xxxx-xxxxxxxxxxxx \
  --role "Contributor" \
  --scope /subscriptions/6446e0c4-xxxx-xxxx-xxxx-xxxxxxxxxxxx/resourceGroups/testama2
```

> **Tip:** If you get `Cannot find user or service principal`, make sure you completed Step 2 first.

---

## Step 4: Create a Federated Credential (OIDC)

This links your GitHub repo to the Azure App Registration so GitHub Actions can authenticate without passwords.

### Option A: Azure Portal

1. Go to **Entra ID** → **App registrations** → select your app
2. Click **Certificates & secrets** → **Federated credentials** → **Add credential**
3. Fill in:
   - **Federated credential scenario:** GitHub Actions deploying Azure resources
   - **Organization:** `<your-github-username>`
   - **Repository:** `<your-repo-name>`
   - **Entity type:** Environment
   - **Environment name:** `production`
   - **Name:** any descriptive name (e.g., `sample-app`)
4. Click **Add**

### Option B: Azure CLI

```bash
az ad app federated-credential create --id <APPLICATION_CLIENT_ID> --parameters '{
  "name": "<credential-name>",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<github-username>/<repo-name>:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### Verify:

```bash
az ad app federated-credential list --id <APPLICATION_CLIENT_ID> -o table
```

---

## Step 5: Create a GitHub Environment

1. Go to your GitHub repo → **Settings** → **Environments**
2. Click **New environment**
3. Name it exactly: `production` (must match the federated credential subject)
4. Click **Configure environment**
5. (Optional) Add protection rules like required reviewers

---

## Step 6: Add GitHub Repository Secrets

Go to your GitHub repo → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**

Add these 3 secrets:

| Secret Name              | Where to Find the Value                                       |
|--------------------------|---------------------------------------------------------------|
| `AZURE_CLIENT_ID`       | Application (client) ID from the App Registration             |
| `AZURE_TENANT_ID`       | Azure AD tenant ID (Entra ID → Overview → Tenant ID)         |
| `AZURE_SUBSCRIPTION_ID` | Azure Portal → Subscriptions → your subscription ID          |

> These are identifiers, not passwords. Authentication is handled via OIDC token exchange.

---

## Step 7: GitHub Actions Workflow Configuration

Key parts of the workflow file (`.github/workflows/deploy.yml`):

### Permissions (workflow level):
```yaml
permissions:
  contents: read
  id-token: write      # Required for OIDC
  security-events: write
```

### Deploy job (must have `id-token: write` at job level too):
```yaml
deploy:
  runs-on: ubuntu-latest
  environment: production
  permissions:
    id-token: write
    contents: read
  steps:
    - name: Azure Login
      uses: azure/login@v2
      with:
        client-id: ${{ secrets.AZURE_CLIENT_ID }}
        tenant-id: ${{ secrets.AZURE_TENANT_ID }}
        subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

    - name: Deploy to Azure Web App
      uses: azure/webapps-deploy@v3
      with:
        app-name: <your-app-service-name>
        package: ./webapp
```

> **Important:** The `id-token: write` permission must be set at the **job level** for the deploy job, not just at the workflow level. Without this, OIDC authentication will silently fail.

---

## Step 8: Run the Workflow

1. Go to GitHub repo → **Actions** tab
2. Select your workflow from the left sidebar
3. Click **Run workflow** → select branch `main` → click **Run workflow**

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `No credentials found` | Azure Login step failed silently | Verify secrets are set + `id-token: write` at job level |
| `Cannot find user or service principal` | Missing Service Principal | Run `az ad sp create --id <APP_ID>` |
| `does not have authorization` | Missing role assignment | Run `az role assignment create` (Step 3) |
| `AADSTS70021: No matching federated identity record found` | Federated credential mismatch | Verify org/repo/environment name matches exactly |

---

## Quick Reference: Get Your IDs

```bash
# Tenant ID
az account show --query tenantId -o tsv

# Subscription ID
az account show --query id -o tsv

# Application (Client) ID — if you know the app name
az ad app list --display-name "github-deploy-sample-app" --query "[0].appId" -o tsv

# Verify federated credentials
az ad app federated-credential list --id <APP_CLIENT_ID> -o table

# Verify role assignments
az role assignment list --assignee <APP_CLIENT_ID> --output table
```

---

## Security Notes

- **No passwords or client secrets are stored** — authentication uses short-lived OIDC tokens
- The federated credential restricts access to **only** your specific repo + environment
- GitHub encrypts all repository secrets — they're masked in logs and cannot be read back
- This is the **most secure** method for GitHub Actions to Azure deployments
