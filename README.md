# Análise de Documentos com Azure AI e Durable Functions

Este projeto implementa uma solução *cloud-native* para processamento assíncrono e inteligente de documentos. A arquitetura utiliza **Azure Functions (Isolated Worker - .NET 8)** com o padrão de orquestração **Durable Functions** para coordenar a extração de texto (OCR) via **Azure Document Intelligence** e análise semântica.

O projeto segue princípios de **Clean Architecture** e **SOLID**, garantindo desacoplamento entre o núcleo de domínio, infraestrutura e a camada de apresentação (Functions).

## 📋 Índice

- [Arquitetura e Fluxo](#arquitetura-e-fluxo)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Tecnologias Utilizadas](#tecnologias-utilizadas)
- [Pré-requisitos de Desenvolvimento](#pré-requisitos-de-desenvolvimento)
- [Configuração Local](#configuração-local)
- [Deployment](#deployment)

## 🏗️ Arquitetura e Fluxo

A solução é orientada a eventos e processa documentos através das seguintes etapas:

1.  **Ingestão:** O cliente submete um documento (binário) via HTTP POST para a Function `SubmeterDocumento`.
2.  **Orquestração:** Uma instância de `OrchestratorFunction` é iniciada.
3.  **OCR (Activity):** O orquestrador aciona a atividade `ExtrairTexto`, que utiliza o **Azure Document Intelligence** (layout prebuilt) para converter o binário em texto estruturado.
4.  **Análise (Activity):** O texto extraído é enviado para a atividade `AnalisarFoundry` (ou serviço de IA configurado) para extração de insights.
5.  **Resiliência:** O processo conta com *Retry Policies* configuradas no orquestrador para lidar com transiência nos serviços cognitivos.

## 📂 Estrutura do Projeto

A solução está organizada para refletir a separação de responsabilidades:

```text
src/
├── AnaliseDocumentos.Core/           # Domínio: Interfaces, Entidades e Contratos (Pure C#)
│   ├── Interfaces/                   # IServicoOcr, IServicoFoundry
│   └── Models/                       # ResultadoAnaliseDocumento
├── AnaliseDocumentos.Infrastructure/ # Implementação: Serviços externos (Azure SDKs)
│   ├── Services/                     # ServicoAzureDocIntel, ServicoAgenteFoundry
│   └── AnaliseDocumentos.Infrastructure.csproj
└── AnaliseDocumentos.Functions/      # Apresentação: Azure Functions (HTTP & Durable)
    ├── Functions/                    # Orquestradores e Activities
    ├── Program.cs                    # Injeção de Dependência e Configuração do Host
    └── host.json
infra/                                # Infraestrutura como Código (Terraform)
```

## 🚀 Tecnologias Utilizadas

- **Runtime:** .NET 8 (Isolated Worker)
- **Computação:** Azure Functions (Linux Consumption Plan)
- **Serviços Cognitivos:** Azure Document Intelligence (Form Recognizer)
- **Orquestração:** Azure Durable Functions
- **Observabilidade:** Application Insights & Log Analytics
- **IaC:** Terraform
- **Segurança:** Azure Key Vault (RBAC e Managed Identity)

## 🛠️ Pré-requisitos de Desenvolvimento

Para executar e depurar localmente:

- **SDK do .NET 8.0**
- **Azure Functions Core Tools v4**
- **Visual Studio 2022** ou **VS Code** (com extensão C# e Azure Functions)
- **Conta Azure Ativa** (para criar recursos de desenvolvimento)
- **Azure CLI** (opcional, para login local)

## 💻 Configuração Local

1.  Clone o repositório.
2.  Navegue até `src/AnaliseDocumentos.Functions`.
3.  Crie um arquivo `local.settings.json` com as configurações necessárias (obtenha os valores do Azure Portal após criar os recursos de dev ou use o Terraform para ambiente de dev):

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "DocIntelEndpoint": "https://<SEU_RECURSO>.cognitiveservices.azure.com/",
    "DocIntelApiKey": "<SUA_CHAVE_DEV>",
    "FoundryProjectEndpoint": "PENDENTE_OU_MOCK",
    "FoundryAgentId": "PENDENTE_OU_MOCK"
  }
}
```

**Nota:** Para emular o Storage localmente, utilize o **Azurite**.

4.  Execute o projeto:
    ```bash
    func start
    ```

## 📦 Deployment

O provisionamento da infraestrutura é totalmente automatizado via Terraform. O código da infraestrutura reside na pasta `infra/` e inclui Resource Groups, Storage Accounts, Function Apps, Key Vaults e permissões RBAC.

Para instruções detalhadas de como realizar o deploy em ambientes de QA ou Produção, consulte o guia dedicado:

👉 **[Guia de Deployment (deploy.md)](./deploy.md)**

## ⚠️ Limitações do Sistema

Para detalhes sobre tamanhos máximos de arquivo, limites de páginas e restrições da IA, consulte o documento:
👉 **[Limitações Técnicas (limitacoes.md)](./limitacoes.md)**