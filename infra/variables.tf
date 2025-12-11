variable "project_name" {
  description = "Nome do projeto, usado para nomear recursos."
  type        = string
  default     = "analisecontratos"
}

variable "resource_group_name" {
  description = "Nome do grupo de recursos."
  type        = string
  default     = "rg-agente-analista-contratos-ia"
}

variable "location" {
  description = "Localização dos recursos na Azure."
  type        = string
  default     = "East US"
}

variable "tags" {
  description = "Tags a serem aplicadas nos recursos."
  type        = map(string)
  default = {
    project = "Agente de Análise de Contratos"
    iac     = "Terraform"
  }
}

variable "model_deployment_name" {
  description = "Nome do deployment do modelo no AI Studio."
  type        = string
  default     = "gpt-4o-mini"
}

variable "model_name" {
    default = "gpt-4o-mini"
}

variable "model_version" {
    default = "1"
}