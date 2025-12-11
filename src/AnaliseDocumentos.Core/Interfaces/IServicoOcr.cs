namespace AnaliseDocumentos.Core.Interfaces;

public interface IServicoOcr
{
    // Recebe bytes diretos para evitar complexidade de stream em Durable Activities
    Task<string> ExtrairTextoAsync(byte[] conteudoArquivo);
}