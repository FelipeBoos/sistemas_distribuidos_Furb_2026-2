namespace Ingressos.Messaging;

// Carrega TP02-IngressosRabbitMQ/.env nas variaveis de ambiente do processo atual. Chamado como
// primeira linha de cada Program.cs, funciona igual rodando via "dotnet run", F5 no Visual
// Studio ou Ctrl+F5 - nao depende de variaveis exportadas no shell nem de launchSettings.json
// (onde credenciais reais vazariam para o Git, ja que esse arquivo normalmente e versionado).
public static class DotEnv
{
    public static void Carregar()
    {
        var caminho = EncontrarArquivoEnv(Directory.GetCurrentDirectory())
            ?? EncontrarArquivoEnv(AppContext.BaseDirectory);

        if (caminho is null)
        {
            return;
        }

        foreach (var linha in File.ReadAllLines(caminho))
        {
            var texto = linha.Trim();
            if (texto.Length == 0 || texto.StartsWith('#'))
            {
                continue;
            }

            var separador = texto.IndexOf('=');
            if (separador <= 0)
            {
                continue;
            }

            var chave = texto[..separador].Trim();
            var valor = texto[(separador + 1)..].Trim().Trim('"');

            // Nao sobrescreve variaveis ja definidas explicitamente no ambiente (ex.: pelo
            // docker-compose, no caminho descartado, ou por quem preferir exportar no shell).
            if (Environment.GetEnvironmentVariable(chave) is null)
            {
                Environment.SetEnvironmentVariable(chave, valor);
            }
        }
    }

    private static string? EncontrarArquivoEnv(string diretorioInicial)
    {
        var diretorio = new DirectoryInfo(diretorioInicial);

        while (diretorio is not null)
        {
            var candidato = Path.Combine(diretorio.FullName, ".env");
            if (File.Exists(candidato))
            {
                return candidato;
            }

            // Marco de que chegamos na raiz do modulo TP02 (mesmo sem achar ".env" ali - para
            // nao continuar subindo e acabar lendo um ".env" de outro projeto do repositorio).
            if (diretorio.Name == "TP02-IngressosRabbitMQ")
            {
                return null;
            }

            diretorio = diretorio.Parent;
        }

        return null;
    }
}
