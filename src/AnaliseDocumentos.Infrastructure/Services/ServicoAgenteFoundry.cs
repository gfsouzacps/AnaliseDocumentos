using AnaliseDocumentos.Core.Interfaces;
using Azure.AI.Agents.Persistent;
using Azure.AI.Projects;
using Azure.Identity;
using Azure;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace AnaliseDocumentos.Infrastructure.Services;

public class ServicoAgenteFoundry : IServicoFoundry
{
    private readonly PersistentAgentsClient _agentsClient;
    private readonly string _agentId;

    public ServicoAgenteFoundry(IConfiguration configuration)
    {
        var projectEndpoint = configuration["FoundryProjectEndpoint"];
        _agentId = configuration["FoundryAgentId"]!;

        ArgumentException.ThrowIfNullOrEmpty(projectEndpoint, "FoundryProjectEndpoint");
        ArgumentException.ThrowIfNullOrEmpty(_agentId, "FoundryAgentId");

        var endpointUri = new Uri(projectEndpoint);
        AIProjectClient projectClient = new(endpointUri, new DefaultAzureCredential());
        _agentsClient = projectClient.GetPersistentAgentsClient();
    }

    public async Task<string> AnalisarTextoAsync(string texto)
    {
        PersistentAgentThread thread = await _agentsClient.Threads.CreateThreadAsync();

        await _agentsClient.Messages.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            texto);

        ThreadRun run = await _agentsClient.Runs.CreateRunAsync(
            thread.Id,
            _agentId);

        do
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            run = await _agentsClient.Runs.GetRunAsync(thread.Id, run.Id);
        }
        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

        if (run.Status != RunStatus.Completed)
        {
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
                // Limpeza acontece aqui, antes de devolver ao orquestrador
                return LimparJsonMarkdown(responseBuilder.ToString());
            }
        }

        return string.Empty;
    }

    // Helper privado: Responsabilidade de limpeza é deste serviço
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