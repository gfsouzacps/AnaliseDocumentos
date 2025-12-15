using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Azure;
using Azure.Identity;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure;
using AnaliseDocumentos.Core.Interfaces;
using AnaliseDocumentos.Infrastructure.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core; // Necessário para configurar limite de tamanho

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // 1. AUMENTAR LIMITE DE UPLOAD (Evita erro com PDFs grandes)
        // Configura o Kestrel para aceitar até 100 MB (limite da Azure Function HTTP)
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 104_857_600; // 100 MB
        });

        // 2. Configuração do Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // 3. Configuração do Blob Storage
        // O pacote Microsoft.Extensions.Azure deve estar instalado
        var storageConnectionString = configuration["AzureWebJobsStorage"];
        ArgumentException.ThrowIfNullOrEmpty(storageConnectionString, "AzureWebJobsStorage");

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.AddBlobServiceClient(storageConnectionString);
        });

        // 4. Configuração do Document Intelligence
        var docIntelEndpoint = configuration["DocIntelEndpoint"];
        var docIntelApiKey = configuration["DocIntelApiKey"];

        ArgumentException.ThrowIfNullOrEmpty(docIntelEndpoint, "DocIntelEndpoint");

        if (!string.IsNullOrEmpty(docIntelApiKey))
        {
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint),
                new AzureKeyCredential(docIntelApiKey)));
        }
        else
        {
            services.AddSingleton(new DocumentAnalysisClient(
                new Uri(docIntelEndpoint),
                new DefaultAzureCredential()));
        }

        // 5. Injeção dos Serviços de Domínio
        services.AddScoped<IServicoOcr, ServicoAzureDocIntel>();
        services.AddScoped<IServicoFoundry, ServicoAgenteFoundry>();
    })
    .Build();

host.Run();