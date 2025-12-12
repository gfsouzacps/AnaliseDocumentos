using AnaliseDocumentos.Core.Interfaces;
using AnaliseDocumentos.Core.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using System.Net;

namespace AnaliseDocumentos.Functions;

public class OrquestradorAnaliseDocumento
{
    private readonly ILogger<OrquestradorAnaliseDocumento> _logger;
    private readonly IServicoOcr _ocrService;
    private readonly IServicoFoundry _foundryService;

    public OrquestradorAnaliseDocumento(
        ILogger<OrquestradorAnaliseDocumento> logger,
        IServicoOcr ocrService,
        IServicoFoundry foundryService)
    {
        _logger = logger;
        _ocrService = ocrService;
        _foundryService = foundryService;
    }

    [Function("SubmeterDocumento")]
    public async Task<HttpResponseData> SubmeterDocumento(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData requisicao,
        [DurableClient] DurableTaskClient cliente)
    {
        _logger.LogInformation("Recebendo documento...");

        using var fluxoMemoria = new MemoryStream();
        await requisicao.Body.CopyToAsync(fluxoMemoria);
        byte[] bytesArquivo = fluxoMemoria.ToArray();

        if (bytesArquivo.Length == 0)
        {
            var respostaRuim = requisicao.CreateResponse(HttpStatusCode.BadRequest);
            await respostaRuim.WriteStringAsync("Arquivo vazio.");
            return respostaRuim;
        }

        string idInstancia = await cliente.ScheduleNewOrchestrationInstanceAsync("OrquestradorDocumento", bytesArquivo);

        _logger.LogInformation($"Orquestração iniciada: {idInstancia}");

        // Retorna 202 Accepted com headers para polling
        return await cliente.CreateCheckStatusResponseAsync(requisicao, idInstancia);
    }

    [Function("OrquestradorDocumento")]
    public async Task<ResultadoAnaliseDocumento> ExecutarOrquestrador([OrchestrationTrigger] TaskOrchestrationContext contexto)
    {
        // Pega o input (bytes do arquivo)
        byte[] bytesArquivo = contexto.GetInput<byte[]>();

        // Política de retentativa para chamadas externas
        var opcoesTentativa = TaskOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5)));

#pragma warning disable CS8600
        // Chamada Activity 1: OCR
        string textoExtraido = await contexto.CallActivityAsync<string>("Activity_ExtrairTexto", bytesArquivo, opcoesTentativa);

        // Chamada Activity 2: Foundry Agent (Já retorna o JSON limpo)
        string jsonAnalise = await contexto.CallActivityAsync<string>("Activity_AnalisarFoundry", textoExtraido);
#pragma warning restore CS8600

        return new ResultadoAnaliseDocumento
        {
            ContratoId = contexto.InstanceId,
            JsonAnalise = jsonAnalise,
            ProcessadoEm = contexto.CurrentUtcDateTime
        };
    }

    [Function("Activity_ExtrairTexto")]
    public async Task<string> ExtrairTexto([ActivityTrigger] byte[] bytesArquivo)
    {
        try
        {
            return await _ocrService.ExtrairTextoAsync(bytesArquivo);
        }
        catch (Azure.RequestFailedException ex)
        {
            // "Encapsula" exceções do Azure para melhor serialização no Durable
            throw new InvalidOperationException($"Erro no OCR (Status {ex.Status}): {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro genérico no OCR: {ex.Message}");
        }
    }

    [Function("Activity_AnalisarFoundry")]
    public async Task<string> AnalisarFoundry([ActivityTrigger] string texto)
    {
        try
        {
            // O código aqui ficou limpo, delegando a responsabilidade total ao serviço
            return await _foundryService.AnalisarTextoAsync(texto);
        }
        catch (Azure.RequestFailedException ex)
        {
            throw new InvalidOperationException($"Erro no Foundry (Status {ex.Status}): {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro genérico no Foundry: {ex.Message}");
        }
    }
}