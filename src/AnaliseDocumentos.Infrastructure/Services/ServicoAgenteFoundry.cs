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
        // Extrai as configurações necessárias, garantindo que não sejam nulas ou vazias.
        var projectEndpoint = configuration["FoundryProjectEndpoint"];
        _agentId = configuration["FoundryAgentId"]!;

        ArgumentException.ThrowIfNullOrEmpty(projectEndpoint, "FoundryProjectEndpoint");
        ArgumentException.ThrowIfNullOrEmpty(_agentId, "FoundryAgentId");
        
        // Cria os clientes usando a identidade AAD (ex: login via AZ CLI, Managed Identity)
        // Os SDKs beta do AI Studio/Projects não suportam AzureKeyCredential diretamente no construtor.
        var endpointUri = new Uri(projectEndpoint);
        AIProjectClient projectClient = new(endpointUri, new DefaultAzureCredential());
        _agentsClient = projectClient.GetPersistentAgentsClient();
    }

    public async Task<string> AnalisarTextoAsync(string texto)
    {
        // 1. Cria uma nova thread de conversação
        PersistentAgentThread thread = await _agentsClient.Threads.CreateThreadAsync();

        // 2. Adiciona a mensagem do usuário (texto do OCR) à thread
        await _agentsClient.Messages.CreateMessageAsync(
            thread.Id,
            MessageRole.User,
            texto);

        // 3. Executa o agente na thread
        ThreadRun run = await _agentsClient.Runs.CreateRunAsync(
            thread.Id,
            _agentId);

        // 4. Aguarda a conclusão da execução
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            run = await _agentsClient.Runs.GetRunAsync(thread.Id, run.Id);
        }
        while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress);

        if (run.Status != RunStatus.Completed)
        {
            var errorMessage = run.LastError?.Message ?? "Unknown error.";
            throw new InvalidOperationException($"Run failed, was canceled, or expired. Status: {run.Status}. Error: {errorMessage}");
        }

        // 5. Recupera as mensagens da thread
        AsyncPageable<PersistentThreadMessage> messages = _agentsClient.Messages.GetMessagesAsync(
            thread.Id, order: ListSortOrder.Descending);

        // 6. Encontra a última resposta do assistente e a retorna
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
                return responseBuilder.ToString();
            }
        }

        return string.Empty; // Retorna vazio se não houver resposta do assistente
    }
}