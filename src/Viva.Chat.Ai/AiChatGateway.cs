using AiChatServer.Contracts.V1;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Viva.Chat.Ai;

/// <summary>AI 서버(gRPC) 호출 확장점. 기본 구현은 AiChatServer 의 StreamChat 입니다.</summary>
public interface IAiChatGateway
{
    IAsyncEnumerable<ChatChunk> StreamChatAsync(ChatRequest request, CancellationToken ct = default);
}

public sealed class GrpcAiChatGateway(
    ChatService.ChatServiceClient client,
    IOptions<VivaAiChatOptions> options) : IAiChatGateway
{
    private readonly VivaAiChatOptions _opt = options.Value;

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_opt.RequestTimeoutSeconds);
        using var call = client.StreamChat(request, deadline: deadline, cancellationToken: ct);

        await foreach (var chunk in call.ResponseStream.ReadAllAsync(ct))
            yield return chunk;
    }
}
