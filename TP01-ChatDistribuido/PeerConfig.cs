using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatDistribuido
{
    public sealed record NodeConfig(
        int Port,
        string Name,
        string AdvertiseHost,
        IReadOnlyList<(string Host, int Port)> KnownPeers);

    public static class PeerConfig
    {
        public static NodeConfig Parse(string[] args)
        {
            var configPathIndex = Array.FindIndex(args, a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));
            if (configPathIndex >= 0 && configPathIndex + 1 < args.Length)
                return ParseFile(args[configPathIndex + 1]);

            return ParseArgs(args);
        }

        private static NodeConfig ParseFile(string path)
        {
            var json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<ConfigFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException($"Arquivo de configuração inválido: {path}");

            var port = raw.Port ?? throw new InvalidDataException("Campo 'port' é obrigatório no arquivo de configuração.");
            var name = raw.Name ?? throw new InvalidDataException("Campo 'name' é obrigatório no arquivo de configuração.");
            var host = raw.Host ?? "127.0.0.1";
            var peers = ParsePeerList(raw.Peers ?? []);

            return new NodeConfig(port, name, host, peers);
        }

        private static NodeConfig ParseArgs(string[] args)
        {
            int? port = null;
            string? name = null;
            var host = "127.0.0.1";
            var peers = Array.Empty<string>();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--port" when i + 1 < args.Length:
                        port = int.Parse(args[++i]);
                        break;
                    case "--name" when i + 1 < args.Length:
                        name = args[++i];
                        break;
                    case "--host" when i + 1 < args.Length:
                        host = args[++i];
                        break;
                    case "--peers" when i + 1 < args.Length:
                        peers = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        break;
                }
            }

            if (port is null)
                throw new ArgumentException("Argumento obrigatório ausente: --port <porta>");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Argumento obrigatório ausente: --name <apelido>");

            return new NodeConfig(port.Value, name, host, ParsePeerList(peers));
        }

        private static readonly Regex SubnetWildcardPattern =
            new(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.\*$", RegexOptions.Compiled);

        /// <summary>Nenhuma conexão real usa essa porta: entradas de peer só com host (sem
        /// porta), usadas para peers de outra rede, sempre têm a porta sobrescrita pela
        /// regra de rede diferente (<see cref="NetworkRules.ResolveDialPort"/>).</summary>
        private const int NoPortPlaceholder = 0;

        private static IReadOnlyList<(string Host, int Port)> ParsePeerList(IEnumerable<string> entries)
        {
            var result = new List<(string, int)>();
            foreach (var entry in entries)
            {
                if (!entry.Contains(':'))
                {
                    result.Add((entry, NoPortPlaceholder));
                    continue;
                }

                var parts = entry.Split(':', 2);
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                    throw new ArgumentException($"Par inválido em 'peers': '{entry}'. Formato esperado host:porta.");

                if (parts[0].EndsWith(".*", StringComparison.Ordinal))
                    ValidateSubnetWildcard(parts[0], entry);

                result.Add((parts[0], port));
            }
            return result;
        }

        private static void ValidateSubnetWildcard(string host, string entry)
        {
            var match = SubnetWildcardPattern.Match(host);
            if (!match.Success ||
                !IsValidOctet(match.Groups[1].Value) ||
                !IsValidOctet(match.Groups[2].Value) ||
                !IsValidOctet(match.Groups[3].Value))
                throw new ArgumentException(
                    $"Faixa de sub-rede inválida em 'peers': '{entry}'. Formato esperado a.b.c.*:porta com octetos 0-255.");
        }

        private static bool IsValidOctet(string value) =>
            int.TryParse(value, out var octet) && octet is >= 0 and <= 255;

        private sealed class ConfigFile
        {
            public int? Port { get; set; }
            public string? Name { get; set; }
            public string? Host { get; set; }
            public List<string>? Peers { get; set; }
        }
    }
}
