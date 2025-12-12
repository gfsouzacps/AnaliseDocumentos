# Guia de Deployment (Versão Simplificada)

Este guia descreve o processo para subir a infraestrutura de um novo cliente de forma segura e isolada.

---

## ⚠️ Regra de Ouro: Isolamento Total

**Nunca reutilize a mesma pasta (ou arquivos `.tfstate`) para clientes diferentes.**

* Novo cliente → **Crie uma nova pasta** copiando apenas os arquivos `.tf`.
* Verifique que **não existem** `terraform.tfstate`, `terraform.tfstate.backup` ou a pasta `.terraform`.
* Inicialize tudo **do zero**.

---

## 1. Pré-requisitos

* **Azure CLI** autenticado na conta do cliente (`az login`).
* **Terraform** instalado.
* Permissões adequadas:

  * Sua conta deve ser **Owner** da assinatura do cliente (recomendado).
  * Para evitar falhas: também ter **User Access Administrator**.

---

## 2. Preparação do Ambiente

1. Copie os arquivos de infraestrutura para uma pasta **exclusiva deste cliente**.
2. Certifique-se de que a pasta está limpa:

   * ❌ `.terraform/`
   * ❌ `terraform.tfstate`
   * ❌ `terraform.tfstate.backup`
3. Configure as variáveis principais no arquivo `infra/variables.tf` ou crie um `terraform.tfvars`:

```
project_name         = "analisejuridico"
resource_group_name  = "rg-analise-juridico"
location             = "eastus2"
```

*Obs.: `project_name` deve ser único globalmente — evite nomes genéricos.*

---

## 3. Execução do Terraform

No terminal, dentro de `infra/`:

```bash
# 1. Inicializa o Terraform (baixa os providers necessários)
terraform init

# 2. Gera o plano de execução
# Confirme se está mostrando: "Plan: X to add, 0 to change, 0 to destroy"
terraform plan -out=tfplan

# 3. Aplica a infraestrutura
terraform apply tfplan
```

---

## 4. Pós-Deploy e Integração com Azure AI

Após o apply, a infraestrutura base e as permissões de "Azure AI Developer" estarão criadas. Agora siga os passos manuais:

### Portal Azure

* Confirme a criação do **Resource Group** e da **Function App**.

### Azure AI Foundry (manual)

1. Crie o **Hub** e o **Projeto** usando o Resource Group criado pelo Terraform.
2. Faça o **deploy do modelo** (ex.: `gpt-4o-mini`).
3. Crie o **Agente** e copie o **Agent ID**.

### Conexão com a Function App

1. Acesse o **Key Vault** criado.
2. Crie/atualize os segredos:

   * `FoundryAgentId`
   * `FoundryProjectEndpoint`
3. Reinicie a **Function App**.

---

## 5. Troubleshooting Rápido

### 🔹 Erros de Permissão (AuthorizationFailed)

* Certifique-se de que você possui:

  * **Owner** da assinatura
  * ou **User Access Administrator**
* Reautentique com `az login` se necessário.

### 🔹 Nome já existente (ex.: *StorageAccount already exists*)

* Ajuste levemente o `project_name`.
* Em muitos casos, rodar o apply novamente funciona, pois o `random_string` gera um novo sufixo.

---