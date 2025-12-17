using AnaliseDocumentos.Core.Interfaces;
using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Azure;
using Microsoft.Extensions.Configuration;
using System.Text;
using Microsoft.Extensions.Logging; // Adicionado para logar o aviso

namespace AnaliseDocumentos.Infrastructure.Services;

public class ServicoAgenteFoundry : IServicoFoundry
{
    private readonly PersistentAgentsClient _agentsClient;
    private readonly string _agentId;
    private readonly ILogger<ServicoAgenteFoundry> _logger; // Injeção de Logger recomendada

    // Limite de segurança: 250k é o hard limit da API. 
    // Vamos usar 200k para garantir margem de segurança e não estourar o contexto do modelo.
    private const int LIMITE_MAXIMO_CARACTERES = 200_000; 

    public ServicoAgenteFoundry(IConfiguration configuration, ILogger<ServicoAgenteFoundry> logger)
    {
        var projectEndpoint = configuration["FoundryProjectEndpoint"];
        _agentId = configuration["FoundryAgentId"]!;
        _logger = logger;

        ArgumentException.ThrowIfNullOrEmpty(projectEndpoint, "FoundryProjectEndpoint");
        ArgumentException.ThrowIfNullOrEmpty(_agentId, "FoundryAgentId");

        var endpointUri = new Uri(projectEndpoint);
        AIProjectClient projectClient = new(endpointUri, new DefaultAzureCredential());
        _agentsClient = projectClient.GetPersistentAgentsClient();
    }

    public async Task<string> AnalisarTextoAsync(string texto)
    {
        // --- PROTEÇÃO CONTRA ESTOURO DE CONTEXTO ---
        if (texto.Length > LIMITE_MAXIMO_CARACTERES)
        {
            _logger.LogWarning($"Texto excedeu o limite do Foundry/Modelo. Tamanho original: {texto.Length}. Truncando para {LIMITE_MAXIMO_CARACTERES}.");
            
            var avisoCorte = $"\n\n[ATENÇÃO DO SISTEMA: O DOCUMENTO ERA MUITO EXTENSO ({texto.Length} caracteres) E FOI CORTADO NESTE PONTO PARA ANÁLISE PARCIAL.]";
            
            // Pega os primeiros 200k caracteres + aviso
            texto = texto.Substring(0, LIMITE_MAXIMO_CARACTERES) + avisoCorte;
        }
        // -------------------------------------------

        PersistentAgentThread thread = await _agentsClient.Threads.CreateThreadAsync();

        await _agentsClient.Messages.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            texto);

        ThreadRun run = await _agentsClient.Runs.CreateRunAsync(
            thread.Id,
            _agentId);

        // Polling de status
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            run = await _agentsClient.Runs.GetRunAsync(thread.Id, run.Id);
        }
        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

        if (run.Status != RunStatus.Completed)
        {
            // Captura erro detalhado se houver
            var errorMessage = run.LastError?.Message ?? "Unknown error.";
            throw new InvalidOperationException($"Run failed: {run.Status}. Error: {errorMessage}");
        }

        AsyncPageable<PersistentThreadMessage> messages = _agentsClient.Messages.GetMessagesAsync(
            thread.Id, order: ListSortOrder.Descending);

        await foreach (PersistentThreadMessage threadMessage in messages)
        {
            if (threadMessage.Role == MessageRole.Agent)
            {
                var responseBuilder = new StringBuilder();
                foreach (MessageContent contentItem in threadMessage.ContentItems)
                {
                    if (contentItem is MessageTextContent textItem)
                    {
                        responseBuilder.Append(textItem.Text);
                    }
                }
                return LimparJsonMarkdown(responseBuilder.ToString());
            }
        }

        return string.Empty;
    }

    private static string LimparJsonMarkdown(string jsonComMarkdown)
    {
        if (string.IsNullOrEmpty(jsonComMarkdown))
            return jsonComMarkdown;

        return jsonComMarkdown
               .Replace("```json", "")
               .Replace("```", "")
               .Trim();
    }
}