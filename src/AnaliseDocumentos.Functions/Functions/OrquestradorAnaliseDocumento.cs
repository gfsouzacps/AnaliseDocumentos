using AnaliseDocumentos.Core.Interfaces;
using AnaliseDocumentos.Core.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
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
    private readonly BlobServiceClient _blobServiceClient;

    public OrquestradorAnaliseDocumento(
        ILogger<OrquestradorAnaliseDocumento> logger,
        IServicoOcr ocrService,
        IServicoFoundry foundryService,
        BlobServiceClient blobServiceClient)
    {
        _logger = logger;
        _ocrService = ocrService;
        _foundryService = foundryService;
        _blobServiceClient = blobServiceClient;
    }

    [Function("SubmeterDocumento")]
    public async Task<HttpResponseData> SubmeterDocumento(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData requisicao,
            [DurableClient] DurableTaskClient cliente)
    {
        _logger.LogInformation("Recebendo documento para upload...");

        if (requisicao.Body == null)
        {
            var respostaRuim = requisicao.CreateResponse(HttpStatusCode.BadRequest);
            await respostaRuim.WriteStringAsync("Corpo da requisição vazio.");
            return respostaRuim;
        }

        try
        {
            // 1. Detectar extensão
            string contentType = "application/octet-stream";
            if (requisicao.Headers.TryGetValues("Content-Type", out var headerValues))
            {
                contentType = headerValues.FirstOrDefault() ?? "application/octet-stream";
            }
            string extensao = ObterExtensaoPorMimeType(contentType);

            // 2. Upload para Blob Storage
            var containerClient = _blobServiceClient.GetBlobContainerClient("documentos-upload");
            await containerClient.CreateIfNotExistsAsync();

            string blobName = $"{Guid.NewGuid()}{extensao}";
            var blobClient = containerClient.GetBlobClient(blobName);

            _logger.LogInformation($"Iniciando upload para Blob: {blobName}...");

            var opcoesUpload = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            };

            await blobClient.UploadAsync(requisicao.Body, opcoesUpload);

            // 3. Obter URL direta (SEM SAS)
            // A "Solução 2" permite que o Doc Intelligence leia direto via RBAC
            Uri urlDireta = blobClient.Uri;

            _logger.LogInformation($"Upload concluído. URL do blob: {urlDireta}");

            // 4. Inicia Orquestração passando a URL limpa
            string idInstancia = await cliente.ScheduleNewOrchestrationInstanceAsync("OrquestradorDocumento", urlDireta.ToString());

            _logger.LogInformation($"Orquestração iniciada: {idInstancia}");

            return await cliente.CreateCheckStatusResponseAsync(requisicao, idInstancia);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no processamento: {Message}", ex.Message);
            var erro = requisicao.CreateResponse(HttpStatusCode.InternalServerError);
            await erro.WriteStringAsync($"Erro interno: {ex.Message}");
            return erro;
        }
    }

    [Function("OrquestradorDocumento")]
    public async Task<ResultadoAnaliseDocumento> ExecutarOrquestrador([OrchestrationTrigger] TaskOrchestrationContext contexto)
    {
        // O input agora é uma URL (string), muito leve
        string urlArquivo = contexto.GetInput<string>()
            ?? throw new ArgumentNullException("URL de entrada nula");

        Uri uriArquivo = new Uri(urlArquivo);

        var opcoesTentativa = TaskOptions.FromRetryPolicy(new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5)));

#pragma warning disable CS8600
        // Activity 1: OCR (Recebe URL)
        string textoExtraido = await contexto.CallActivityAsync<string>("Activity_ExtrairTexto", uriArquivo, opcoesTentativa);

        // Activity 2: Foundry (Recebe Texto)
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
    public async Task<string> ExtrairTexto([ActivityTrigger] Uri urlArquivo)
    {
        try
        {
            return await _ocrService.ExtrairTextoAsync(urlArquivo);
        }
        catch (Azure.RequestFailedException ex)
        {
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

    // Método auxiliar mantido AQUI (camada de entrada HTTP)
    private static string ObterExtensaoPorMimeType(string mimeType)
    {
        return mimeType.ToLower().Trim() switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/tiff" => ".tiff",
            "image/bmp" => ".bmp",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/msword" => ".doc",
            _ => ".bin"
        };
    }
}