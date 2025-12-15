# Etapas Manuais para Configuração do Azure AI Studio

Este documento descreve as etapas manuais necessárias para configurar os recursos do Azure AI Studio e atualizar a configuração da sua aplicação.

## Pré-requisitos

- A infraestrutura base (Resource Group, Key Vault, Storage Account, etc.) foi provisionada com sucesso via `terraform apply`.
- Você tem as permissões necessárias na sua assinatura do Azure para criar recursos de Machine Learning.

## Passo 1: Criar/Identificar seus Recursos de AI

O script Terraform provisionou a infraestrutura *core*. Você precisa agora criar os recursos de AI Studio manualmente e/ou coletar a informação dos recursos que você já criou.

1.  **Navegue até o [Azure AI Studio](https://ai.azure.com/)**.
2.  **Crie um AI Hub** se ainda não tiver um. Associe-o ao `resource_group`, `storage account`, `key vault`, etc. criados pelo Terraform.
3.  **Crie um AI Project** dentro do seu Hub.

## Passo 2: Coletar Variáveis e Atualizar a Aplicação

Após ter seu agente criado e implantado, você precisa de **três** informações.

1.  **ID do Agente (Agent ID):**
    - No seu projeto do AI Studio, vá para a seção de agentes.
    - Selecione o seu agente (`Agente analista de Documentos`).
    - O ID do agente (algo como `asst_...`) é geralmente visível nos detalhes ou na URL.
    - **Você forneceu:** `asst_aOPFQY2M79SYXp5XdXL8AGFw`

2.  **Endpoint do Projeto (Project Endpoint):**
    - No seu projeto do AI Studio, procure pelas configurações de desenvolvimento ou do projeto.
    - Você precisa da URL base da API para o projeto.
    - **Você forneceu:** `https://ia-agentes-foundry.services.ai.azure.com/api/projects/proj-default`

3.  **Endpoint do Modelo e Chave (OpenAI-compatible):**
    - Estes são os valores que você obteve ao implantar um modelo como `gpt-4o-mini` e que são usados para chamadas diretas de API, não para o SDK do agente. Embora o novo código não os use diretamente, é bom guardá-los.
    - **Você forneceu o Endpoint:** `https://ia-agentes-foundry.cognitiveservices.azure.com/openai/deployments/gpt-4o-mini/chat/completions?api-version=2025-01-01-preview`
    - **Você forneceu a Chave:** `3e6z...` (chave que você postou)

## Passo 3: Configurar a Azure Function

Você precisa adicionar o **ID do Agente** e o **Endpoint do Projeto** nas configurações da sua Azure Function (`func-analisecontratos...`). O método recomendado é usar o Key Vault.

1.  **Navegue até o seu Key Vault** (`kv-analisecontratos...`) no Azure Portal.
2.  Vá para a seção **Segredos**.
3.  **Crie/Atualize os seguintes segredos:**
    - **`FoundryProjectEndpoint`**:
        - **Valor do segredo**: `https://ia-agentes-foundry.services.ai.azure.com/api/projects/proj-default`
    - **`FoundryAgentId`**:
        - **Valor do segredo**: `asst_aOPFQY2M79SYXp5XdXL8AGFw`
    - **`FoundryApiKey`** (Opcional, para referência futura):
        - **Valor do segredo**: A chave de API longa (`3e6z...`) que você salvou.

4.  **Garanta que a Function App esteja configurada para usar estes segredos.**
    - O Terraform já configurou a identidade da Function App para ler do Key Vault. Agora, você só precisa garantir que os *nomes das configurações do aplicativo* correspondam aos *nomes dos segredos* no Key Vault.
    - Vá para a sua **Function App** -> **Configuração**.
    - Verifique se as seguintes configurações de aplicativo existem. Se não, adicione-as:
        - **Nome**: `FoundryProjectEndpoint`
          **Valor**: `@Microsoft.KeyVault(SecretUri=URL_DO_SEGREDO_FoundryProjectEndpoint)`
        - **Nome**: `FoundryAgentId`
          **Valor**: `@Microsoft.KeyVault(SecretUri=URL_DO_SEGREDO_FoundryAgentId)`
        - **Nome**: `FoundryApiKey`
          **Valor**: `@Microsoft.KeyVault(SecretUri=URL_DO_SEGREDO_FoundryApiKey)`

    - Você pode obter a `URL_DO_SEGREDO` clicando na versão atual de cada segredo no Key Vault e copiando o "Identificador do Segredo".

## Passo 4: Configurar Permissões (RBAC)

A identidade da sua Function App precisa de permissão para interagir com o AI Project.

1.  Navegue até o **AI Project** que você está usando (`proj-default`).
2.  Vá para **Gerenciar acesso** (ou `Access control (IAM)`).
3.  Clique em **Adicionar** -> **Adicionar atribuição de função**.
4.  **Atribuir a função:**
    - **Função**: `Azure AI Developer`
    - **Atribuir acesso a**: `Identidade gerenciada`
    - **Membros**: Selecione a identidade gerenciada da sua Function App (o nome deve ser `func-analisecontratos...`).
5.  Clique em **Revisar + atribuir**.

Após concluir estas etapas, sua aplicação estará totalmente configurada para usar o novo SDK do Agente.