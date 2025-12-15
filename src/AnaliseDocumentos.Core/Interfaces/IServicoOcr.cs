namespace AnaliseDocumentos.Core.Interfaces;

public interface IServicoOcr
{
    Task<string> ExtrairTextoAsync(Uri urlArquivo);
}