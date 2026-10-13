# Decision-1 scope gate for Agent Framework

.NET console demo: Microsoft-Decision-1 gates requests to a customer support
agent for the fictional Contoso Shop.

## Pipeline

```text
Latest request + recent history -> AF run middleware -> Decision-1
    in_scope     -> support ChatClientAgent
    out_of_scope -> predefined reply; no support-model call
```

Decision-1 classifies the latest request using a native `choice` question.
Earlier turns resolve follow-ups; they do not make unrelated topic changes in scope.

- The gate receives recent user turns, including the current request, and their
  assistant replies. The support agent receives the full conversation.
- Both branches preserve exchanges in the same in-memory AF session.
- Decision API failures stop the request rather than open the gate.

The app runs one conversation sequentially, without customer-record access or
transaction tools. Model-based scope classification is not a security boundary.

## Run

Requires the .NET 10 SDK, a Decision-1 deployment, a chat-completions-capable
support model deployment, and their API keys. AF is installed from NuGet.

Configure the variables in [.env.example](./.env.example):

```bash
cp -n .env.example .env
# Edit .env with endpoints, API keys, and deployment names.
set -a
source .env
set +a
dotnet run
```

The app reads environment variables, not the file directly. Re-export after edits.
Both `*_MODEL` values must be **deployment names**, not underlying model names:
for a deployment named `Decision-1`, set `DECISION_MODEL=Decision-1`.

Endpoints accept HTTPS resource roots or full inference URLs:

```bash
DECISION_ENDPOINT=https://your-gateway.azure-api.net/your-resource/providers/microsoft/v1/systemone
SUPPORT_ENDPOINT=https://your-resource.openai.azure.com/openai/v1
```

Gateway prefixes are preserved; a trailing slash is optional. Resource roots
have the corresponding API path appended. Project URLs, query strings, fragments,
and embedded credentials are not accepted.

`SCOPE_CONTEXT_TURNS` sets the gate's window: 5-10 user turns, default 8.
Decision-1 uses the [native decision API](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/use-foundry-models-microsoft-decision),
not chat completions. Requests include `Content-Length` for gateway compatibility.

Commands: `/reset` clears session history; `/exit` quits; Ctrl+C cancels and exits.

## Example sequence

| Request | Routing |
| --- | --- |
| Can I return headphones I bought from Contoso last week? | `in_scope` |
| What if I already opened the box? | `in_scope` |
| Write me a Python web scraper. | `out_of_scope` |
| Back to my headphones: how long would a refund take? | `in_scope` |
| `/reset`, then: Who won the World Cup? | `out_of_scope` |

The console reports the decision, context size, latency, and support-model routing.
