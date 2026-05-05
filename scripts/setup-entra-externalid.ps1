param(
  [Parameter(Mandatory = $true)]
  [string]$tenantId,

  [Parameter(Mandatory = $true)]
  [string]$subscriptionId,

  [Parameter(Mandatory = $true)]
  [string]$rg,

  [Parameter(Mandatory = $true)]
  [string]$webAppName,

  [Parameter(Mandatory = $true)]
  [string]$appRegName,

  [Parameter(Mandatory = $true)]
  [string]$tenantDomain,

  [Parameter(Mandatory = $true)]
  [string]$instance,

  [string]$policyId = 'B2C_1_susi'
)

$ErrorActionPreference = 'Stop'

$graphToken = az account get-access-token --tenant $tenantId --resource-type ms-graph --query accessToken -o tsv
if (-not $graphToken) { throw 'Could not acquire Graph token for External ID tenant.' }

$headers = @{ Authorization = "Bearer $graphToken"; 'Content-Type' = 'application/json' }

$filter = [uri]::EscapeDataString("displayName eq '$appRegName'")
$appList = Invoke-RestMethod -Method GET -Uri "https://graph.microsoft.com/v1.0/applications?`$filter=$filter" -Headers $headers
$app = $appList.value | Select-Object -First 1

if (-not $app) {
  $createBody = @{
    displayName = $appRegName
    signInAudience = 'AzureADMyOrg'
    web = @{
      redirectUris = @(
        'https://localhost:5001/signin-oidc',
        "https://$webAppName.azurewebsites.net/signin-oidc"
      )
      logoutUrl = "https://$webAppName.azurewebsites.net/signout-callback-oidc"
      implicitGrantSettings = @{
        enableIdTokenIssuance = $true
        enableAccessTokenIssuance = $false
      }
    }
  } | ConvertTo-Json -Depth 10

  $app = Invoke-RestMethod -Method POST -Uri 'https://graph.microsoft.com/v1.0/applications' -Headers $headers -Body $createBody
} else {
  $patchBody = @{
    web = @{
      redirectUris = @(
        'https://localhost:5001/signin-oidc',
        "https://$webAppName.azurewebsites.net/signin-oidc"
      )
      logoutUrl = "https://$webAppName.azurewebsites.net/signout-callback-oidc"
      implicitGrantSettings = @{
        enableIdTokenIssuance = $true
        enableAccessTokenIssuance = $false
      }
    }
  } | ConvertTo-Json -Depth 10

  Invoke-RestMethod -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" -Headers $headers -Body $patchBody | Out-Null
}

$appDetails = Invoke-RestMethod -Method GET -Uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" -Headers $headers
$appRoles = @($appDetails.appRoles)
if (-not $appRoles) { $appRoles = @() }

function NewRole([string]$displayName, [string]$value) {
  return [ordered]@{
    allowedMemberTypes = @('User')
    description = "$displayName role"
    displayName = $displayName
    id = [guid]::NewGuid().Guid
    isEnabled = $true
    origin = 'Application'
    value = $value
  }
}

if (-not ($appRoles | Where-Object { $_.value -eq 'App.Admin' })) {
  $appRoles += NewRole -displayName 'App Admin' -value 'App.Admin'
}
if (-not ($appRoles | Where-Object { $_.value -eq 'App.Reader' })) {
  $appRoles += NewRole -displayName 'App Reader' -value 'App.Reader'
}

$rolePatch = @{ appRoles = $appRoles } | ConvertTo-Json -Depth 20
Invoke-RestMethod -Method PATCH -Uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" -Headers $headers -Body $rolePatch | Out-Null

$spFilter = [uri]::EscapeDataString("appId eq '$($app.appId)'")
$spList = Invoke-RestMethod -Method GET -Uri "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=$spFilter" -Headers $headers
$sp = $spList.value | Select-Object -First 1
if (-not $sp) {
  $spBody = @{ appId = $app.appId } | ConvertTo-Json
  $sp = Invoke-RestMethod -Method POST -Uri 'https://graph.microsoft.com/v1.0/servicePrincipals' -Headers $headers -Body $spBody
}

$me = Invoke-RestMethod -Method GET -Uri 'https://graph.microsoft.com/v1.0/me' -Headers $headers
$rolesNow = (Invoke-RestMethod -Method GET -Uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" -Headers $headers).appRoles
$adminRoleId = ($rolesNow | Where-Object { $_.value -eq 'App.Admin' } | Select-Object -First 1).id
$readerRoleId = ($rolesNow | Where-Object { $_.value -eq 'App.Reader' } | Select-Object -First 1).id

$existing = Invoke-RestMethod -Method GET -Uri "https://graph.microsoft.com/v1.0/users/$($me.id)/appRoleAssignments" -Headers $headers
$existingIds = @($existing.value | Where-Object { $_.resourceId -eq $sp.id } | ForEach-Object { $_.appRoleId })

foreach ($rid in @($adminRoleId, $readerRoleId)) {
  if ($existingIds -notcontains $rid) {
    $assignBody = @{
      principalId = $me.id
      resourceId = $sp.id
      appRoleId = $rid
    } | ConvertTo-Json
    Invoke-RestMethod -Method POST -Uri "https://graph.microsoft.com/v1.0/users/$($me.id)/appRoleAssignments" -Headers $headers -Body $assignBody | Out-Null
  }
}

$pwdBody = @{ passwordCredential = @{ displayName = 'sampleapp-secret'; endDateTime = (Get-Date).AddYears(1).ToString('o') } } | ConvertTo-Json -Depth 5
$pwdResp = Invoke-RestMethod -Method POST -Uri "https://graph.microsoft.com/v1.0/applications/$($app.id)/addPassword" -Headers $headers -Body $pwdBody
$clientSecret = $pwdResp.secretText

az account set --subscription $subscriptionId
az webapp config appsettings set --resource-group $rg --name $webAppName --settings `
  EntraExternalId__Instance=$instance `
  EntraExternalId__Domain=$tenantDomain `
  EntraExternalId__TenantId=$tenantId `
  EntraExternalId__ClientId=$($app.appId) `
  EntraExternalId__ClientSecret=$clientSecret `
  EntraExternalId__SignUpSignInPolicyId=$policyId `
  EntraExternalId__CallbackPath=/signin-oidc `
  EntraExternalId__SignedOutCallbackPath=/signout-callback-oidc --output none

Remove-Item -Force -ErrorAction SilentlyContinue .\publish.zip
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue .\publish

dotnet publish SampleApp/SampleApp.csproj -c Release -o .\publish | Out-Null
Compress-Archive -Path .\publish\* -DestinationPath .\publish.zip -Force
az webapp deploy --resource-group $rg --name $webAppName --src-path .\publish.zip --type zip --restart true --output none

$caStatus = 'Failed'
$caPolicyId = $null
try {
  $caBody = @{
    displayName = "CA-$webAppName-RequireMFA-ReportOnly"
    state = 'enabledForReportingButNotEnforced'
    conditions = @{
      users = @{ includeUsers = @($me.id); excludeUsers = @() }
      applications = @{ includeApplications = @($app.appId); excludeApplications = @() }
      clientAppTypes = @('all')
    }
    grantControls = @{ operator = 'OR'; builtInControls = @('mfa') }
  } | ConvertTo-Json -Depth 20

  $ca = Invoke-RestMethod -Method POST -Uri 'https://graph.microsoft.com/v1.0/identity/conditionalAccess/policies' -Headers $headers -Body $caBody
  $caStatus = 'Created'
  $caPolicyId = $ca.id
} catch {
  $caStatus = "Failed: $($_.Exception.Message)"
}

[pscustomobject]@{
  TenantId = $tenantId
  SubscriptionId = $subscriptionId
  WebApp = $webAppName
  WebAppUrl = "https://$webAppName.azurewebsites.net"
  AppRegistrationDisplayName = $appRegName
  AppClientId = $app.appId
  ServicePrincipalObjectId = $sp.id
  SignedInUser = $me.userPrincipalName
  ConditionalAccess = $caStatus
  ConditionalAccessPolicyId = $caPolicyId
} | ConvertTo-Json -Depth 5