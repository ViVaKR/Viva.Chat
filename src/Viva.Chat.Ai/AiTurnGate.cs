using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

namespace Viva.Chat.Ai;

/// <summary>진행 중인 턴 하나. 끝나면 반드시 DisposeAsync 로 반납합니다.</summary>
public interface IAiTurn : IAsyncDisposable
{
    /// <summary>사용자가 Cancel 하거나 서버가 내려가면 취소됩니다.</summary>
    CancellationToken Token { get; }
}

/// <summary>
/// "세션당 진행 중인 턴은 1개" 를 지키고, 진행 중인 턴을 취소하는 확장점.
/// 기본 구현은 메모리 안에서만 동작합니다 (레플리카 1개일 때 충분).
/// 레플리카가 여럿이면 ViVaKR.Chat.Redis 의 구현(Redis 락 + 취소 신호)으로 교체합니다. (UseAiTurnGate)
/// </summary>
public interface IAiTurnGate
{
    /// <summary>턴을 시작한다. 같은 sessionKey 로 진행 중인 턴이 있으면 null.</summary>
    ValueTask<IAiTurn?> TryBeginAsync(string sessionKey, CancellationToken ct = default);

    /// <summary>진행 중인 턴을 취소한다. 취소할 턴이 없으면 false.</summary>
    bool Cancel(string sessionKey);
}

public sealed class InMemoryAiTurnGate(IHostApplicationLifetime lifetime) : IAiTurnGate
{
    private readonly ConcurrentDictionary<string, Turn> _turns = new(StringComparer.Ordinal);

    public ValueTask<IAiTurn?> TryBeginAsync(string sessionKey, CancellationToken ct = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        var turn = new Turn(this, sessionKey, cts);

        if (_turns.TryAdd(sessionKey, turn))
            return ValueTask.FromResult<IAiTurn?>(turn);

        cts.Dispose();
        return ValueTask.FromResult<IAiTurn?>(null);
    }

    public bool Cancel(string sessionKey)
    {
        if (!_turns.TryGetValue(sessionKey, out var turn)) return false;
        turn.Cancel();
        return true;
    }

    private void Release(Turn turn)
        => _turns.TryRemove(new KeyValuePair<string, Turn>(turn.Key, turn));

    private sealed class Turn(InMemoryAiTurnGate gate, string key, CancellationTokenSource cts) : IAiTurn
    {
        public string Key { get; } = key;
        public CancellationToken Token => cts.Token;

        public void Cancel()
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { /* 이미 끝난 턴 */ }
        }

        public ValueTask DisposeAsync()
        {
            gate.Release(this);
            cts.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
