using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using AnaliseDocumentos.Core.Interfaces;
using AnaliseDocumentos.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Configuração Document Intelligence (Fail-Fast)
        var docIntelEndpoint = configuration["DocIntelEndpoint"];
        var docIntelApiKey = configuration["DocIntelApiKey"];

        ArgumentException.ThrowIfNullOrEmpty(docIntelEndpoint, "DocIntelEndpoint");

        if (!string.IsNullOrEmpty(docIntelApiKey))
        {
            // Usa chave de API se fornecida
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint), 
                new AzureKeyCredential(docIntelApiKey)));
        }
        else
        {
            // Caso contrário, usa Identidade Gerenciada
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint), 
                new DefaultAzureCredential()));
        }

        // Injeção dos serviços da aplicação
        services.AddScoped<IServicoOcr, ServicoAzureDocIntel>();
        services.AddScoped<IServicoFoundry, ServicoAgenteFoundry>();
    })
    .Build();

host.Run();
