# Decision-1 scope gate for an Agent Framework support agent

A small .NET 10 console demo: **Microsoft-Decision-1 decides whether a
customer request belongs in support before the main model is called.**
The support agent serves a fictional consumer-electronics retailer, Contoso Shop.
There are no backend integrations, tools, test projects, or deployment infrastructure.

## Design

```text
User message + recent AF session history
                  |
         AF agent-run middleware
                  |
     Decision-1: in_scope / out_of_scope
                  |
       +----------+----------+
       |                     |
   in_scope             out_of_scope
       |                     |
AF ChatClientAgent      Fixed response
support chat model      No support model call
       |                     |
       +----------+----------+
                  |
         Update AF session history
```

This is **pipeline middleware**, not an agent handoff. The decision model makes
a bounded classification, not a conversational response, so a separate agent or
workflow would add machinery without adding value.

- The outer middleware reads the same `InMemoryChatHistoryProvider` used by the
  support agent. It sends the latest **8 user turns by default**, including the
  current request, and the assistant replies associated with those turns.
  Configure 5-10 turns. Early in a conversation it sends whatever is available.
- The gate classifies the **latest request**; earlier messages resolve references
  such as "What about the warranty?" They must not excuse an unrelated topic change.
- Rejected requests **and their fixed replies** are explicitly saved to the AF
  session because short-circuiting bypasses the inner agent's history write.
  Accepted exchanges are saved by AF normally, without duplicate writes.
- Decision-1 uses a native `choice` question with two labels. The selected label
  controls the gate; this demo does not apply an unvalidated confidence threshold.
- HTTP errors, invalid JSON, missing answers, and unexpected labels are errors,
  **never permission to call the support agent**. The console reports the error;
  it does not disguise a failed classification as an out-of-scope answer.
- The console uses non-streaming `RunAsync`. AF also routes `RunStreamingAsync`
  through the supplied non-streaming middleware, so streaming would not bypass
  the gate, but would only emit the completed response.

The full conversation remains in memory until `/reset` or process exit. Only
the classifier's input is windowed; the support agent retains the full session.
The demo runs one conversation sequentially, not a concurrent multi-user service.

## Foundry APIs

Decision-1 is **not a chat-completions model**. As documented in the
[Microsoft-Decision-1 quickstart](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision),
the demo calls:

```text
POST https://<resource>/providers/microsoft/v1/systemone
api-key: <DECISION_API_KEY>
```

`DECISION_ENDPOINT` can be that complete URL, including an API gateway's path
prefix, or a resource root to which the app appends the decision path.
The JSON request is buffered so .NET sends `Content-Length`, as required by
gateways that reject streamed/chunked request bodies.

Its request has this structure (instructions shortened):

```json
{
  "model": "<decision deployment name>",
  "state": {
    "conversation": [
      { "role": "user", "content": "Can I return my headphones?" },
      { "role": "assistant", "content": "Our demo return window is 30 days." },
      { "role": "user", "content": "What if I already opened them?" }
    ]
  },
  "questions": {
    "scope": {
      "type": "choice",
      "instructions": "Classify the last user message using the support scope.",
      "criteria": {
        "in_scope": "Within the support scope",
        "out_of_scope": "Outside the support scope"
      }
    }
  }
}
```

The code reads `answers.scope.type` (`choice`) and `answers.scope.choice`
(`in_scope` or `out_of_scope`). Other response fields are not needed for this demo.
The `model` in the request is your **deployment name**, not necessarily
`microsoft-decision-1`.
For example, if Foundry shows **Name: Decision-1** and
**Model name: Microsoft-Decision-1**, set `DECISION_MODEL=Decision-1`.

The support model uses the Foundry OpenAI-compatible chat-completions endpoint:

```text
https://<resource>/openai/v1/
```

`SUPPORT_ENDPOINT` can be that complete base URL (with or without the trailing
slash), or a resource root. The app normalizes the trailing slash and does not
append `/openai/v1/` twice.

Use a chat-completions-capable generative model for support. Both clients use
resource API keys for simple environment-only configuration. No `az login` is
required. Never put real keys in the example file or commit your `.env`.

## Run

Prerequisites: .NET 10 SDK, a deployed Microsoft-Decision-1 model, and a deployed
chat-completions model. Agent Framework 1.24.0 comes from NuGet; the source checkout
is not required to build or run this demo.

```bash
cp .env.example .env
# Edit .env with your resource endpoints, API keys, and deployment names.
set -a
source .env
set +a

dotnet run
```

The app does not load `.env` automatically; the shell commands export its values.
Alternatively set the variables directly in your shell or IDE launch environment.

| Variable | Meaning |
| --- | --- |
| `DECISION_ENDPOINT` | HTTPS resource root or full Decision-1 inference URL |
| `DECISION_API_KEY` | API key for that resource |
| `DECISION_MODEL` | Decision-1 deployment name |
| `SUPPORT_ENDPOINT` | HTTPS resource root or full OpenAI `/openai/v1` base URL |
| `SUPPORT_API_KEY` | API key for that resource |
| `SUPPORT_MODEL` | Support chat model deployment name |
| `SCOPE_CONTEXT_TURNS` | Optional integer from 5 through 10; default 8 |

Endpoints can be resource roots, such as
`https://your-resource.services.ai.azure.com/` or
`https://your-resource.openai.azure.com/`, or full inference URLs, for example:

```bash
DECISION_ENDPOINT=https://your-gateway.azure-api.net/your-resource/providers/microsoft/v1/systemone
SUPPORT_ENDPOINT=https://your-resource.openai.azure.com/openai/v1
```

Gateway prefixes are preserved. Full URLs must end in the corresponding API
path; project URLs (`/api/projects/...`) are not supported. Do not include query
strings, fragments, or embedded credentials. The decision and support settings
can use the same resource and key.

Commands: `/reset` clears both models' conversational context; `/exit` quits;
Ctrl+C cancels a running request. `dotnet run -- --help` shows configuration
requirements without credentials or model calls.

## Demo walkthrough

Try these in the same conversation:

1. `Can I return headphones I bought from Contoso last week?`
   - In scope: Decision-1 allows the support model to answer.
2. `What if I already opened the box?`
   - Contextual follow-up: the classifier sees the previous exchange.
3. `Write me a Python web scraper.`
   - Out of scope, despite the previous support context: fixed reply, no support call.
4. `Back to my headphones: how long would a refund take?`
   - In scope again: rejection does not permanently lock the conversation.
5. `/reset`, then `Who won the World Cup?`
   - Out of scope with a fresh session.

Each request prints the decision label, number of context turns/messages,
decision latency, and whether the support model will be called:

```text
[Decision-1] out_of_scope | 3 user turns, 5 messages | <latency> ms | support model NOT called
Support> I can only help with Contoso Shop orders, delivery, returns, refunds, billing, account access, and product setup or troubleshooting. Please ask a question about one of those topics.
```

These are expected scenarios, not claims of measured model accuracy. Live routing
and responses must be checked once you connect your deployments.

## Code map

- [Program.cs](./Program.cs): configuration, AF agent construction, console loop.
- [ScopeGate.cs](./ScopeGate.cs): recent-context selection, middleware short-circuit,
  and persistence of rejected exchanges.
- [DecisionClient.cs](./DecisionClient.cs): native Decision-1 HTTP request and
  strict validation of the returned label.
- [SupportPolicy.cs](./SupportPolicy.cs): shared scope definition, fixed reply,
  and the support agent's fictional knowledge.

Edit the shared policy to change the business scope. Model-based scope detection
is not a security boundary or a replacement for production safety controls.
Decision-1 can misclassify requests; validate wording and behavior on realistic
traffic before relying on the gate outside a tech demo.

## Validation

```bash
dotnet build
dotnet run --no-build -- --help
```

No live Foundry validation is possible without configured deployments and keys.
No unit tests or offline fake-model mode are included.

## Troubleshooting

Decision API errors include the HTTP status and response body (with the API key
redacted and long bodies truncated). An error never opens the scope gate.

- **DeploymentNotFound (400/404):** check `DECISION_MODEL` against the deployment's
  **Name** in Foundry, not its underlying **Model name**. After editing `.env`,
  export the settings again with `set -a; source .env; set +a`.
- **400 with validation details:** inspect the reported field and request shape.
- **401/403:** verify the key and gateway/resource access.

## References

- [Decision-1 launch article](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/introducing-microsoft-decision-1-in-microsoft-foundry-for-decision-and-classific/4562742)
- [Official deployment and inference quickstart](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision)
- [AF middleware sample](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/Agents/Agent_Step11_Middleware)
