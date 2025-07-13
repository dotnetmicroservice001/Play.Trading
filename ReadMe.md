# Play.Trading - Trading Microservice

The Trading microservice manages user trades within the Play Economy system. It coordinates complex operations like item purchases using a **Saga pattern** and **state machine**, ensuring consistency across distributed services.

## 🔁 Features

- Handles item purchases and user-to-user trades
- Orchestrates workflows using **Saga with state machines**
- Integrates with **Inventory**, **Catalog**, and **Identity** services
- Uses **SignalR** to provide real-time trade updates to frontend clients
- Secured via **OAuth 2.0** and **JWT tokens** issued by the Identity microservice

## 🧱 Tech Stack

- ASP.NET Core
- MassTransit + State Machine (Saga)
- SignalR
- RabbitMQ (event messaging)
- Duende IdentityServer (OAuth 2.0 / OpenID Connect)


## Create and Publish Package
```bash
version="1.0.2"
owner="dotnetmicroservice001"
gh_pat="[YOUR_PERSONAL_ACCESS_TOKEN]"

dotnet pack src/Play.Catalog.Contracts --configuration Release \
  -p:PackageVersion="$version" \
  -p:RepositoryUrl="https://github.com/$owner/Play.Catalog" \
  -o ../Packages
  
dotnet nuget push ../Packages/Play.Catalog.Contracts.$version.nupkg --api-key $gh_pat \
--source "github"
```

## Build a Docker Image
```bash
export version=1.0.3
export GH_OWNER=dotnetmicroservice001
export GH_PAT="ghp_YourRealPATHere"
export appname="playeconomyapp"
docker build --secret id=GH_OWNER --secret id=GH_PAT -t "$appname.azurecr.io/play.trading:$version" .
```

## Run Docker Container
```bash 
export cosmosDbConnString="conn string here"
export serviceBusConnString="conn string here"
docker run -it --rm \
  -p 5006:5006 \
  --name trading \
  -e MongoDbSettings__ConnectionString=$cosmosDbConnString \
  -e ServiceBusSettings__ConnectionString=$serviceBusConnString \
  -e ServiceSettings__MessageBroker="SERVICEBUS" \
  play.trading:$version
```

## Publishing Docker Image
```bash 
az acr login --name $appname
docker push "$appname.azurecr.io/play.trading:$version"
```
## 🐳 Build & Push Docker Image (M2 Mac + AKS Compatible)

Build a multi-architecture image (ARM64 for local M2 Mac, AMD64 for AKS) and push to ACR:
```bash
version="1.0.4"
export GH_OWNER=dotnetmicroservice001
export GH_PAT="ghp_YourRealPATHere"
export appname="playeconomyapp"
az acr login --name $appname
docker buildx build \
  --platform linux/amd64 \
  --secret id=GH_OWNER --secret id=GH_PAT \
  -t "$appname.azurecr.io/play.trading:$version" \
  --push .
```

## Create Kubernetes namespace
```bash 
export namespace="trading"
kubectl create namespace $namespace 
```
## Create the Kubernetes Pod
```bash
kubectl apply -f ./kubernetes/${namespace}.yaml -n "$namespace"
```

## Creating Azure Managed Identity and granting it access to Key Vault Store
```bash
export appname=playeconomyapp
az identity create --resource-group $appname --name $namespace 

export IDENTITY_CLIENT_ID=$(az identity show -g "$appname" -n "$namespace" --query clientId -o tsv)
export SUBSCRIPTION_ID=$(az account show --query id -o tsv)

az role assignment create \
  --assignee "$IDENTITY_CLIENT_ID" \
  --role "Key Vault Secrets User" \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$appname/providers/Microsoft.KeyVault/vaults/$appname"
```

## Establish the related Identity Credential
```bash
export AKS_OIDC_ISSUER="$(az aks show -n $appname -g $appname --query "oidcIssuerProfile.issuerUrl" -otsv)"
az identity federated-credential create --name ${namespace} --identity-name "${namespace}" --resource-group "${appname}" --issuer "${AKS_OIDC_ISSUER}" --subject system:serviceaccount:"${namespace}":"${namespace}-serviceaccount" --audience api://AzureADTokenExchange
```

## install helm chart
```bash 
helmUser="00000000-0000-0000-0000-000000000000"
helmPassword=$(az acr login --name $appname --expose-token --output tsv --query accessToken)
helm registry login $appname.azurecr.io --username $helmUser --password $helmPassword 

chartVersion="0.1.0"
helm upgrade "$namespace-service" oci://$appname.azurecr.io/helm/microservice --version $chartVersion -f ./helm/values.yaml -n $namespace --install
```

---
