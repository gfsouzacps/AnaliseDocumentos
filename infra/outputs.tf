output "function_app_name" {
  description = "O nome da Azure Function App."
  value       = azurerm_linux_function_app.func.name
}

output "function_app_default_hostname" {
  description = "O hostname padrão da Azure Function App."
  value       = azurerm_linux_function_app.func.default_hostname
}

output "key_vault_uri" {
  description = "A URI do Key Vault."
  value       = azurerm_key_vault.kv.vault_uri
}
