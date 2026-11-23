using System.Text;
using AiChatServer.Contracts.V1;
using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viva.Chat.Contracts;
using AiMessage = AiChatServer.Contracts.V1.ChatMessage;
using ChatMessage = Viva.Chat.Contracts.ChatMessage;

namespace Viva.Chat.Ai;

/// <summary>턴 하나를 실행하는 데 필요한 값. 질문은 허브가 이미 저장한 상태입니다.</summary>
public sealed record AiTurnRequest(
    string SiteId,
    string Room,
    string SessionId,
    string Group,
    ChatMessage UserMessage,
    string StreamId);

/// <summary>
/// AI 답변 한 턴을 백그라운드에서 실행해 SignalR 그룹으로 흘려보낸다.
/// 허브 메서드는 바로 돌아가므로 같은 연결에서 Cancel 같은 다른 호출을 받을 수 있다.
/// </summary>
public sealed class AiChatSessionRunner(
    IServiceScopeFactory scopes,
    IHubContext<AiChatHub, IAiChatClient> hubContext,
    IOptions<VivaAiChatOptions> options,
    TimeProvider time,
    ILogger<AiChatSessionRunner> logger)
{
    private readonly VivaAiChatOptions _opt = options.Value;

    /// <summary>기다리지 않고 바로 돌아온다. turn 은 끝날 때 이 러너가 반납한다.</summary>
    public void Start(AiTurnRequest request, IAiTurn turn)
        => _ = Task.Run(() => RunAsync(request, turn));

    private async Task RunAsync(AiTurnRequest r, IAiTurn turn)
    {
        await using (turn)
        {
            try
            {
                // 저장소(IChatStore)는 보통 Scoped 라서 턴마다 새 스코프를 만든다.
                await using var scope = scopes.CreateAsyncScope();
                await RunCoreAsync(scope.ServiceProvider, r, turn.Token);
            }
            catch (Exception ex)
            {
                // 여기까지 올라오면 알림조차 못 보낸 상황. 백그라운드 작업이라 삼키지 않고 로그로 남긴다.
                logger.LogError(ex, "AI 턴 처리 중 예상하지 못한 오류 site={Site} session={Session}", r.SiteId, r.SessionId);
            }
        }
    }

    private async Task RunCoreAsync(IServiceProvider sp, AiTurnRequest r, CancellationToken ct)
    {
        var store = sp.GetRequiredService<IChatStore>();
        var gateway = sp.GetRequiredService<IAiChatGateway>();
        var clients = hubContext.Clients.Group(r.Group);

        var content = new StringBuilder();
        Usage? usage = null;
        var started = time.GetTimestamp();

        try
        {
            var request = await BuildRequestAsync(store, r, ct);

            await foreach (var chunk in gateway.StreamChatAsync(request, ct))
            {
                if (chunk.Done)
                {
                    usage = chunk.Usage;
                    continue;
                }

                if (string.IsNullOrEmpty(chunk.Delta)) continue;

                switch (chunk.Kind)
                {
                    case ChunkKind.Thinking when _opt.EmitThinking:
                        await clients.ReceiveChunk(r.SessionId, r.StreamId, AiChunkKind.Thinking, chunk.Delta);
                        break;

                    case ChunkKind.Content:
                        content.Append(chunk.Delta);
                        await clients.ReceiveChunk(r.SessionId, r.StreamId, AiChunkKind.Content, chunk.Delta);
                        break;
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // 사용자가 Cancel 했거나 서버가 내려가는 중.
            await FinishCancelledAsync(store, r, content.ToString(), clients);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI 답변을 받지 못했습니다 site={Site} session={Session}", r.SiteId, r.SessionId);
            await clients.StreamFailed(r.SessionId, r.StreamId, ToUserMessage(ex));
            return;
        }

        await FinishAsync(store, r, content.ToString(), clients);

        // 질문/답변 본문은 개인적인 내용이 많으니 로그에 남기지 않는다.
        logger.LogInformation("AI 턴 완료 site={Site} session={Session} {Ms}ms tokens={Prompt}/{Completion}",
            r.SiteId, r.SessionId, (long)time.GetElapsedTime(started).TotalMilliseconds,
            usage?.PromptTokens, usage?.CompletionTokens);
    }

    // ---- 요청 조립 ---------------------------------------------------------

    private async Task<ChatRequest> BuildRequestAsync(IChatStore store, AiTurnRequest r, CancellationToken ct)
    {
        // 방금 저장한 질문이 이력에 포함되어 돌아오므로 +1 을 읽고, Id 로 걸러서 마지막에 한 번만 붙인다.
        // (본문 비교 대신 Id 로 거르면 같은 질문을 반복해도 안전하다)
        var history = _opt.ContextHistoryCount > 0
            ? await store.GetRecentAsync(r.SiteId, r.Room, _opt.ContextHistoryCount + 1, ct)
            : [];

        var request = new ChatRequest
        {
            ConversationId = r.Group,
            Persona = _opt.DefaultPersona
        };

        foreach (var m in history)
        {
            if (m.Id == r.UserMessage.Id || string.IsNullOrWhiteSpace(m.Text)) continue;

            request.Messages.Add(new AiMessage
            {
                // 이름이 아니라 UserId 로 AI 발화를 구분한다. (사용자가 필명을 "법등" 으로 지어도 AI 로 취급되지 않는다)
                Role = AiChatDefaults.IsAiUserId(m.UserId) ? ChatRole.Assistant : ChatRole.User,
                Content = m.Text
            });
        }

        // AiChatServer 규약: 마지막 메시지는 반드시 user
        request.Messages.Add(new AiMessage { Role = ChatRole.User, Content = r.UserMessage.Text });
        return request;
    }

    // ---- 마무리 ---------------------------------------------------------

    private async Task FinishAsync(IChatStore store, AiTurnRequest r, string text, IAiChatClient clients)
    {
        var finalText = text.Trim();
        if (finalText.Length == 0)
        {
            await clients.StreamFailed(r.SessionId, r.StreamId, "AI 가 빈 답변을 보냈습니다. 다시 시도하세요.");
            return;
        }

        var message = CreateAiMessage(r, finalText);

        // 사용자가 연결을 끊어도 완성된 답변은 끝까지 저장한다.
        try
        {
            await store.SaveAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI 답변을 저장하지 못했습니다 site={Site} session={Session}", r.SiteId, r.SessionId);
            await clients.StreamFailed(r.SessionId, r.StreamId, "답변을 저장하지 못했습니다. 잠시 후 다시 시도하세요.");
            return;
        }

        await clients.StreamCompleted(r.SessionId, r.StreamId, message);
    }

    private async Task FinishCancelledAsync(IChatStore store, AiTurnRequest r, string text, IAiChatClient clients)
    {
        ChatMessage? partial = null;
        var trimmed = text.Trim();

        if (_opt.SavePartialOnCancel && trimmed.Length > 0)
        {
            var message = CreateAiMessage(r, trimmed);
            try
            {
                await store.SaveAsync(message, CancellationToken.None);
                partial = message;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "중단된 답변을 저장하지 못했습니다 site={Site} session={Session}", r.SiteId, r.SessionId);
            }
        }

        await clients.StreamCancelled(r.SessionId, r.StreamId, partial);
    }

    /// <summary>Id 를 streamId 와 같게 만들어서, 클라이언트가 스트리밍 말풍선을 최종 메시지로 그대로 교체할 수 있게 한다.</summary>
    private ChatMessage CreateAiMessage(AiTurnRequest r, string text) => new(
        r.StreamId,
        r.SiteId,
        r.Room,
        _opt.AiDisplayName,
        text,
        time.GetUtcNow(),
        UserId: AiChatDefaults.AiUserId(_opt.DefaultPersona),
        Avatar: null);

    /// <summary>사용자에게 보이는 메시지. 내부 예외 내용(서버 주소, 스택)은 로그에만 남기고 보내지 않는다.</summary>
    private static string ToUserMessage(Exception ex) => ex switch
    {
        RpcException { StatusCode: StatusCode.Unavailable } => "AI 서버에 연결할 수 없습니다. 잠시 후 다시 시도하세요.",
        RpcException { StatusCode: StatusCode.DeadlineExceeded } => "AI 응답 시간이 초과되었습니다. 질문을 줄여서 다시 시도하세요.",
        _ => "AI 응답 중 오류가 발생했습니다. 잠시 후 다시 시도하세요."
    };
}
