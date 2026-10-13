using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

internal sealed class ScopeGate(DecisionClient decisionClient, InMemoryChatHistoryProvider history, int contextTurns)
{
    internal async Task<AgentResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var incoming = messages.ToList();
        if (incoming.Count != 1 || incoming[0].Role != ChatRole.User || string.IsNullOrWhiteSpace(incoming[0].Text))
        {
            throw new ArgumentException("This demo expects one non-empty user message per run.", nameof(messages));
        }

        var stored = history.GetMessages(session);
        var conversation = stored.Concat(incoming)
            .Where(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant)
            .ToList();
        var userIndexes = conversation.Select((message, index) => (message, index))
            .Where(item => item.message.Role == ChatRole.User)
            .Select(item => item.index)
            .ToList();
        var firstIndex = userIndexes[Math.Max(0, userIndexes.Count - contextTurns)];
        var window = conversation.Skip(firstIndex).ToList();
        var turns = Math.Min(userIndexes.Count, contextTurns);

        var timer = Stopwatch.StartNew();
        // Errors propagate: an unavailable or malformed decision must never open the gate.
        var inScope = await decisionClient.IsInScopeAsync(window, cancellationToken);
        timer.Stop();
        Console.WriteLine($"[Decision-1] {(inScope ? "in_scope" : "out_of_scope")} | " +
            $"{turns} user turns, {window.Count} messages | {timer.ElapsedMilliseconds} ms | " +
            (inScope ? "calling support agent" : "support model NOT called"));

        if (inScope)
        {
            return await innerAgent.RunAsync(incoming, session, options, cancellationToken);
        }

        var rejection = new ChatMessage(ChatRole.Assistant, SupportPolicy.OutOfScopeReply);
        // Short-circuiting skips AF's normal history write, so persist this exchange ourselves.
        history.SetMessages(session, [.. stored, .. incoming, rejection]);
        return new AgentResponse(rejection);
    }
}
