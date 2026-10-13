using System.ClientModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

if (args.Length != 0)
{
    Console.WriteLine("Run with dotnet run. Configuration comes from environment variables.");
    Console.WriteLine("Required: DECISION_ENDPOINT, DECISION_API_KEY, DECISION_MODEL,");
    Console.WriteLine("          SUPPORT_ENDPOINT, SUPPORT_API_KEY, SUPPORT_MODEL.");
    Console.WriteLine("Optional: SCOPE_CONTEXT_TURNS (5-10, default 8).");
    return args is ["--help"] ? 0 : 1;
}

try
{
    var decisionEndpoint = InferenceEndpoint("DECISION_ENDPOINT", "/providers/microsoft/v1/systemone");
    var decisionKey = Required("DECISION_API_KEY");
    var decisionModel = Required("DECISION_MODEL");
    var supportEndpoint = InferenceEndpoint("SUPPORT_ENDPOINT", "/openai/v1/");
    var supportKey = Required("SUPPORT_API_KEY");
    var supportModel = Required("SUPPORT_MODEL");
    var contextTurns = ContextTurns();

    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var decisionClient = new DecisionClient(httpClient, decisionEndpoint, decisionKey, decisionModel);
    var history = new InMemoryChatHistoryProvider();
    using IChatClient supportClient = new OpenAIClient(
        new ApiKeyCredential(supportKey),
        new OpenAIClientOptions { Endpoint = supportEndpoint })
        .GetChatClient(supportModel)
        .AsIChatClient();

    AIAgent supportAgent = new ChatClientAgent(supportClient, new ChatClientAgentOptions
    {
        Name = "ContosoSupport",
        Description = "A basic customer support agent for the fictional Contoso Shop.",
        ChatHistoryProvider = history,
        ChatOptions = new ChatOptions { Instructions = SupportPolicy.AgentInstructions }
    });
    var gate = new ScopeGate(decisionClient, history, contextTurns);
    AIAgent agent = supportAgent.AsBuilder().Use(gate.RunAsync, null).Build();
    AgentSession session = await agent.CreateSessionAsync();

    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    Console.WriteLine("Contoso Shop support | Agent Framework + Microsoft-Decision-1");
    Console.WriteLine($"Gate: {decisionModel} | Support: {supportModel} | Context: {contextTurns} user turns");
    Console.WriteLine("Ask about orders, delivery, returns, billing, accounts, or product setup.");
    Console.WriteLine("Commands: /reset starts a new conversation; /exit quits. Ctrl+C cancels.");
    Console.WriteLine();

    while (!shutdown.IsCancellationRequested)
    {
        Console.Write("You> ");
        var input = Console.ReadLine();
        if (input is null || input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        if (input.Equals("/reset", StringComparison.OrdinalIgnoreCase))
        {
            session = await agent.CreateSessionAsync(shutdown.Token);
            Console.WriteLine("Conversation reset (including scope-gate history).\n");
            continue;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            continue;
        }

        try
        {
            var response = await agent.RunAsync(input, session, cancellationToken: shutdown.Token);
            Console.WriteLine($"Support> {response.Text}\n");
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            break;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Request timed out. No fallback response was generated; try again.\n");
        }
        catch (Exception ex) when (ex is HttpRequestException or ClientResultException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Request failed: {ex.Message}");
            Console.Error.WriteLine("No fallback response was generated. Check the deployments/configuration and try again.\n");
        }
    }

    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Startup failed: {ex.Message}");
    Console.Error.WriteLine("See README.md or run dotnet run -- --help for configuration.");
    return 1;
}

static string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : throw new InvalidOperationException($"Set {name}.");
}

static Uri InferenceEndpoint(string name, string inferencePath)
{
    var value = Required(name);
    if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
        endpoint.Scheme != Uri.UriSchemeHttps ||
        string.IsNullOrEmpty(endpoint.Host) ||
        endpoint.Query.Length != 0 ||
        endpoint.Fragment.Length != 0 ||
        endpoint.UserInfo.Length != 0)
    {
        throw new InvalidOperationException(
            $"{name} must be an HTTPS resource root or an inference URL ending in {inferencePath} " +
            "(without a query, fragment, or embedded credentials).");
    }

    if (endpoint.AbsolutePath == "/")
    {
        return new Uri(endpoint, inferencePath.TrimStart('/'));
    }

    var path = endpoint.AbsolutePath.TrimEnd('/');
    if (!path.EndsWith(inferencePath.TrimEnd('/'), StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{name} must be a resource root or an inference URL ending in {inferencePath}, not a project URL.");
    }

    return new UriBuilder(endpoint)
    {
        Path = inferencePath.EndsWith('/') ? path + "/" : path
    }.Uri;
}

static int ContextTurns()
{
    var value = Environment.GetEnvironmentVariable("SCOPE_CONTEXT_TURNS") ?? "8";
    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var turns) || turns is < 5 or > 10)
    {
        throw new InvalidOperationException("SCOPE_CONTEXT_TURNS must be an integer from 5 through 10.");
    }

    return turns;
}
