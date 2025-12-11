namespace AnaliseDocumentos.Core.Interfaces;

public interface IServicoFoundry
{
    Task<string> AnalisarTextoAsync(string texto);
}