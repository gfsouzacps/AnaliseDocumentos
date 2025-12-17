# Limitações Técnicas e Fronteiras do Sistema

Este documento descreve as restrições operacionais, limites de capacidade e fronteiras da arquitetura atual da solução **Análise de Documentos**. Estes limites são derivados das cotas da Azure, das capacidades dos modelos de IA (LLMs) e das configurações de segurança da aplicação.

## 1. Ingestão de Arquivos (Upload)

| Característica | Limite Atual | Motivo Técnico | Comportamento ao Exceder |
| :--- | :--- | :--- | :--- |
| **Tamanho Máximo de Arquivo** | **100 MB** | Limite rígido do Load Balancer da Azure Functions (HTTP Trigger). | A requisição falha imediatamente (Connection Reset ou 413 Payload Too Large) antes de chegar ao código. |
| **Extensões Suportadas** | PDF, JPG, JPEG, PNG, TIFF, BMP, DOCX, DOC. | Mapeamento explícito de MIME Types no orquestrador. | O arquivo é salvo como `.bin` no Storage, podendo causar falha na etapa de OCR. |
| **Timeout de Upload** | ~2 a 4 minutos | Timeout padrão de requisições HTTP na Azure. | O cliente recebe um erro de Timeout (504) se a sua internet for lenta para subir 100MB. |

> **Recomendação para arquivos > 100MB:** A arquitetura deve evoluir para o padrão "Direct Upload to Blob" (o Frontend sobe direto pro Storage via SAS Token), acionando a Function via *BlobTrigger* em vez de *HttpTrigger*.

## 2. Processamento de IA (Azure OpenAI / Foundry)

O modelo utilizado é o `gpt-4o-mini`. Embora robusto, ele possui limites físicos de quantidade de texto que consegue "ler" de uma vez.

| Característica | Limite Configurado | Motivo Técnico | Comportamento ao Exceder |
| :--- | :--- | :--- | :--- |
| **Máximo de Caracteres (Input)** | **200.000 caracteres** (~50 a 70 páginas densas) | Limite do endpoint de mensagens da API do Azure Foundry (256k) e Janela de Contexto do Modelo (128k tokens). | **Truncamento Seguro:** O sistema corta automaticamente o texto excedente e adiciona um aviso: `[ATENÇÃO: O DOCUMENTO FOI CORTADO...]`. A análise será feita apenas sobre a parte inicial. |
| **Alucinação / Precisão** | Variável | Modelos probabilísticos podem gerar informações incorretas ("alucinações"). | O sistema reproduz a resposta da IA. A validação humana final é obrigatória para dados críticos. |
| **Tempo de Resposta** | Variável (10s a 3min) | Depende da carga da Azure OpenAI e do tamanho do texto. | O sistema usa processamento assíncrono (Durable Functions). O cliente deve consultar o status (Polling). |

## 3. Extração de Texto (OCR - Document Intelligence)

| Característica | Limite | Motivo Técnico | Comportamento |
| :--- | :--- | :--- | :--- |
| **Qualidade da Imagem** | 50x50 pixels (mínimo) | Requisito do motor de OCR da Azure. | Imagens muito pequenas ou borradas não terão texto extraído. |
| **PDFs com Senha** | Não suportado | O serviço não possui a chave para descriptografar. | Erro na Activity de Extração de Texto. |
| **Arquivos Digitalizados (Scaneados)** | Suportado | O modelo `prebuilt-layout` lida bem com imagens. | O processo é mais lento que PDFs nativos de texto. |

## 4. Infraestrutura e Custos

* **Plano de Consumo (Serverless):** A Function pode "dormir" se ficar inativa (Cold Start). A primeira requisição do dia pode levar ~10 segundos a mais.
* **Retentativas (Retry Policy):** O sistema tenta processar o OCR e a IA até **3 vezes** em caso de falhas temporárias da Azure. Se falhar 3 vezes, o processo é marcado como `Failed`.
* **Armazenamento:** Os arquivos são mantidos no Blob Storage. Uma política de ciclo de vida (Lifecycle Management) deve ser configurada futuramente para apagar arquivos antigos e economizar custos.

## 5. Matriz de Compatibilidade (Resumo)

Para garantir 100% de sucesso na análise, o documento deve idealmente seguir:

* **Formato:** PDF Nativo (gerado por computador) ou DOCX.
* **Tamanho:** Até 30MB (recomendado para performance rápida).
* **Extensão:** Até 50 páginas (para caber inteiro na janela de contexto da IA sem cortes).
* **Legibilidade:** Texto nítido, sem rasuras manuais graves.

---
*Documento gerado pela Equipe de Arquitetura - Versão 1.0*