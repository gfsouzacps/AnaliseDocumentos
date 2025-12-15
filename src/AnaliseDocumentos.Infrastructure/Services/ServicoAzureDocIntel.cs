using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using AnaliseDocumentos.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AnaliseDocumentos.Infrastructure.Services;

public class ServicoAzureDocIntel : IServicoOcr
{
    private readonly DocumentAnalysisClient _client;
    private readonly ILogger<ServicoAzureDocIntel> _logger;

    public ServicoAzureDocIntel(DocumentAnalysisClient client, ILogger<ServicoAzureDocIntel> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> ExtrairTextoAsync(Uri urlArquivo)
    {
        _logger.LogInformation("Iniciando OCR no Document Intelligence...");

        Operation<AnalyzeResult> operacao = await _client.AnalyzeDocumentFromUriAsync(
            WaitUntil.Completed,
            "prebuilt-layout", 
            urlArquivo);

        AnalyzeResult resultado = operacao.Value;
        var sb = new StringBuilder();

        // Estratégia de concatenação simples preservando quebras de linha
        foreach (var paragrafo in resultado.Paragraphs)
        {
            sb.AppendLine(paragrafo.Content);
        }

        _logger.LogInformation($"OCR concluído. Extraídos {sb.Length} caracteres.");
        return sb.ToString();
    }
}