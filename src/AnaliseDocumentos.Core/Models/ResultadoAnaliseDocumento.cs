namespace AnaliseDocumentos.Core.Models;

public class ResultadoAnaliseDocumento
{
    public string ContratoId { get; set; } = string.Empty;
    public string TextoPuro { get; set; } = string.Empty; // Opcional, para debug
    public string JsonAnalise { get; set; } = string.Empty; // Retorno do Foundry
    public DateTime ProcessadoEm { get; set; }
}