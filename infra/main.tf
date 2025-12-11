terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.102.0"
    }
    azapi = {
      source  = "Azure/azapi"
      version = "~> 1.12.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.5.1"
    }
  }
}

provider "azurerm" {
  features {
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
    key_vault {
      purge_soft_delete_on_destroy = true
    }
  }
}

provider "azapi" {}

# Para garantir nomes unicos
resource "random_string" "sufixo" {
  length  = 3
  special = false
  upper   = false
}

# 1. Resource Group
resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
  tags     = var.tags
}

# 2. Key Vault
resource "azurerm_key_vault" "kv" {
  name                     = "kv-${var.project_name}-${random_string.sufixo.result}"
  location                 = azurerm_resource_group.rg.location
  resource_group_name      = azurerm_resource_group.rg.name
  tenant_id                = data.azurerm_client_config.current.tenant_id
  sku_name                 = "standard"
  enable_rbac_authorization = true # Usando RBAC para politicas de acesso

  tags = var.tags
}

# 3. Storage Account
resource "azurerm_storage_account" "st" {
  name                     = "st${var.project_name}${random_string.sufixo.result}"
  resource_group_name      = azurerm_resource_group.rg.name
  location                 = azurerm_resource_group.rg.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  tags                     = var.tags
}

# 4. Document Intelligence
resource "azurerm_cognitive_account" "doc_intel" {
  name                = "cog-docintel-${var.project_name}-${random_string.sufixo.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  kind                = "FormRecognizer"
  sku_name            = "S0" # Tier Standard
  tags                = var.tags
}


resource "azurerm_log_analytics_workspace" "log_analytics" {
  name                = "log-${var.project_name}-${random_string.sufixo.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = var.tags
}

# Recursos de suporte para o AI Hub
resource "azurerm_application_insights" "ai" {
  name                = "appi-${var.project_name}-${random_string.sufixo.result}"
  location            = azurerm_resource_group.rg.location
  resource_group_name = azurerm_resource_group.rg.name
  application_type    = "web"
  workspace_id        = azurerm_log_analytics_workspace.log_analytics.id # Explicitly link to Log Analytics Workspace
  tags                = var.tags
}

resource "azurerm_container_registry" "cr" {
  name                = "cr${var.project_name}${random_string.sufixo.result}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = var.tags
}


# 6. Azure Function
resource "azurerm_service_plan" "plan" {
  name                = "asp-${var.project_name}"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  os_type             = "Linux"
  sku_name            = "Y1" # Consumption
}

resource "azurerm_linux_function_app" "func" {
  name                       = "func-${var.project_name}-${random_string.sufixo.result}"
  resource_group_name        = azurerm_resource_group.rg.name
  location                   = azurerm_resource_group.rg.location
  storage_account_name       = azurerm_storage_account.st.name
  storage_account_access_key = azurerm_storage_account.st.primary_access_key
  service_plan_id            = azurerm_service_plan.plan.id
  https_only                 = true

  site_config {
    application_stack {
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
    application_insights_key = azurerm_application_insights.ai.instrumentation_key
    ftps_state               = "FtpsOnly"
  }

  identity {
    type = "SystemAssigned"
  }

  app_settings = {
    "AzureWebJobsStorage"      = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.st_conn_string.id})"
    "FUNCTIONS_WORKER_RUNTIME" = "dotnet-isolated"
    "DocIntelEndpoint"         = azurerm_cognitive_account.doc_intel.endpoint
    "DocIntelApiKey"           = "@Microsoft.KeyVault(SecretUri=${azurerm_key_vault_secret.docintel_api_key.id})"
    "FoundryApiUrl"            = "PENDENTE"
    "FoundryApiKey"            = "PENDENTE"
  }

  tags = var.tags
  depends_on = [
    azurerm_key_vault_secret.st_conn_string,
    azurerm_key_vault_secret.docintel_api_key
  ]
}

data "azurerm_client_config" "current" {}

# 7. Segredos no Key Vault
resource "azurerm_key_vault_secret" "st_conn_string" {
  name         = "StorageConnectionString"
  value        = azurerm_storage_account.st.primary_connection_string
  key_vault_id = azurerm_key_vault.kv.id
}

resource "azurerm_key_vault_secret" "docintel_api_key" {
  name         = "DocIntelApiKey"
  value        = azurerm_cognitive_account.doc_intel.primary_access_key
  key_vault_id = azurerm_key_vault.kv.id
}


# 8. Permissões (RBAC)
# Permissão para a Function App ler segredos do Key Vault
resource "azurerm_role_assignment" "func_kv_reader" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_linux_function_app.func.identity[0].principal_id
}

# Permissão para o usuário/sp logado poder adicionar segredos
resource "azurerm_role_assignment" "current_user_kv_admin" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Permissão para o usuário/sp logado poder ler os segredos (para o Terraform validar o estado)
resource "azurerm_role_assignment" "current_user_kv_reader" {
  scope                = azurerm_key_vault.kv.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = data.azurerm_client_config.current.object_id
}

# Permissão para a Function acessar o Document Intelligence
resource "azurerm_role_assignment" "func_doc_intel_role" {
  scope                = azurerm_cognitive_account.doc_intel.id
  role_definition_name = "Cognitive Services User"
  principal_id         = azurerm_linux_function_app.func.identity[0].principal_id
}