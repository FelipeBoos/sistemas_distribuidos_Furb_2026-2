using System.Text.Json;

namespace Ingressos.Messaging;

// Opcoes de serializacao compartilhadas por TODO caminho que (de)serializa uma mensagem - publish
// normal, outbox e RPC. Usar instancias diferentes de JsonSerializerOptions em produtor e
// consumidor (ex.: camelCase de um lado, PascalCase padrao do outro) faz propriedades nao
// baterem silenciosamente e voltarem com o valor default, sem erro de deserializacao.
public static class JsonSerializacao
{
    public static readonly JsonSerializerOptions Opcoes = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
