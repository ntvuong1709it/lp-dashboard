# Deploying to Azure on a small budget

Target: Azure Container Apps (API, scales to zero) + Azure Database for PostgreSQL Flexible Server (B1ms) + static frontend.
Rough cost: a few dollars a month for the API and frontend, plus about $12 to $15 a month for Postgres unless your
subscription still has the free 12-month B1ms allowance. Check the numbers in your own subscription before relying on them.

1. Create a Base RPC key (Alchemy or similar free tier). The public RPC is too limited for scanning logs.
2. Set a budget alert first: Cost Management > Budgets > $40 with an email alert.
3. Resource group and database:
   az group create -n lp-ledger -l southeastasia
   az postgres flexible-server create -g lp-ledger -n lp-ledger-db --tier Burstable --sku-name Standard_B1ms --storage-size 32 --version 16
4. API, built and deployed from source (creates a container registry for you):
   cd backend
   az containerapp up -n lp-ledger-api -g lp-ledger --source . --ingress external --target-port 8080 \
     --env-vars BASE_RPC_URL=secretref:rpc
   (add the secret first: az containerapp secret set -n lp-ledger-api -g lp-ledger --secrets rpc=<your RPC URL>)
5. Keep it cheap: min replicas 0, max replicas 1.
   az containerapp update -n lp-ledger-api -g lp-ledger --min-replicas 0 --max-replicas 1
6. Frontend: deploy `web/` to Azure Static Web Apps (free plan) and point its API calls at the Container App URL.
