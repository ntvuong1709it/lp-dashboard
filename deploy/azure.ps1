<#
Deploys the single-container image to Azure App Service (Linux) via Azure Container Registry.
Prereqs: Azure CLI (`az login` done) and a BASE_RPC_URL in the environment. Run from the repo root:

  $env:BASE_RPC_URL = "https://base-mainnet.g.alchemy.com/v2/<key>"
  ./deploy/azure.ps1 -Name lpdash-yourname

Re-running builds a new image and restarts the app; existing settings and the database are kept.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{2,38}$')][string]$Name,   # globally unique; used for the app and (without dashes) the registry
    [string]$ResourceGroup = "rg-$Name",
    [string]$Location = "westeurope",
    [string]$Sku = "B1"
)
$ErrorActionPreference = "Stop"
if (-not $env:BASE_RPC_URL) { throw "Set `$env:BASE_RPC_URL first (it is stored as an app setting, never in the repo)." }

$acr = ($Name -replace '-', '') + "acr"
$plan = "plan-$Name"

az group create -n $ResourceGroup -l $Location | Out-Null
az acr create -g $ResourceGroup -n $acr --sku Basic --admin-enabled true | Out-Null

# Build in Azure (no local Docker needed).
az acr build -g $ResourceGroup -r $acr -t "lp-dashboard:latest" .
if ($LASTEXITCODE) { throw "Image build failed." }

az appservice plan create -g $ResourceGroup -n $plan --is-linux --sku $Sku | Out-Null
$exists = az webapp list -g $ResourceGroup --query "[?name=='$Name'].name" -o tsv
if (-not $exists) {
    az webapp create -g $ResourceGroup -p $plan -n $Name -i "$acr.azurecr.io/lp-dashboard:latest" | Out-Null
}
$cred = az acr credential show -n $acr | ConvertFrom-Json
az webapp config container set -g $ResourceGroup -n $Name `
    --container-image-name "$acr.azurecr.io/lp-dashboard:latest" `
    --container-registry-url "https://$acr.azurecr.io" `
    --container-registry-user $cred.username `
    --container-registry-password $cred.passwords[0].value | Out-Null

# The database lives on /home, which App Service persists across restarts and deployments.
# SMB-backed storage cannot do SQLite WAL, so use DELETE journaling (same as the Windows bind mount in docker-compose).
$settings = @(
    "WEBSITES_PORT=8080",
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE=true",
    "ConnectionStrings__Default=Data Source=/home/data/lpdashboard.db",
    "Database__SqliteJournalMode=DELETE",
    # TLS ends at Azure's proxy: trust X-Forwarded-Proto/For so the login cookie is Secure and the limiter sees the real IP.
    "ASPNETCORE_FORWARDEDHEADERS_ENABLED=true",
    "Evm__Chains__0__RpcUrl=$env:BASE_RPC_URL",
    "Backup__Enabled=false"
)
if ($env:TELEGRAM_BOT_TOKEN) { $settings += "Alerts__Telegram__BotToken=$env:TELEGRAM_BOT_TOKEN", "Alerts__Telegram__ChatId=$env:TELEGRAM_CHAT_ID" }
az webapp config appsettings set -g $ResourceGroup -n $Name --settings $settings | Out-Null

# One instance only: the background sync and the SQLite file must not be shared between instances.
az webapp config set -g $ResourceGroup -n $Name --always-on true --generic-configurations '{\"healthCheckPath\": \"/health\"}' | Out-Null
az webapp restart -g $ResourceGroup -n $Name | Out-Null

Write-Host "Deployed: https://$Name.azurewebsites.net  (first start runs migrations; watch: az webapp log tail -g $ResourceGroup -n $Name)"
