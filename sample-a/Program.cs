using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

//## IF OLLAMA
using OllamaSharp;
//## ELSE IF GITHUB_MODELS || OPENAI || AZURE_OPENAI
using OpenAI;
using System.ClientModel;
//## ENDIF

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

//## IF GITHUB_MODELS
var credential = new ApiKeyCredential(builder.Configuration["GitHubModels:Token"] ?? throw new InvalidOperationException("Missing configuration: GitHubModels:Token. See the README for details."));
var openAIOptions = new OpenAIClientOptions()
{
    Endpoint = new Uri("https://models.inference.ai.azure.com")
};

var ghModelsClient = new OpenAIClient(credential, openAIOptions);
var chatClient = ghModelsClient.GetChatClient("gpt-4o-mini").AsIChatClient();

////## ELSE IF OPENAI
//var openAIClient = new OpenAIClient(
//    new ApiKeyCredential(builder.Configuration["OpenAI:Key"] ?? throw new InvalidOperationException("Missing configuration: OpenAI:Key. See the README for details.")));

//#pragma warning disable OPENAI001 // GetOpenAIResponseClient(string) is experimental and subject to change or removal in future updates.
//var chatClient = openAIClient.GetOpenAIResponseClient("gpt-4o-mini").AsIChatClient();
//#pragma warning restore OPENAI001

////## ELSE IF OLLAMA
//IChatClient chatClient = new OllamaApiClient(new Uri("http://localhost:11434"),
//    "llama3.2");
//IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new OllamaApiClient(new Uri("http://localhost:11434"),
//    "all-minilm");

////## ELSE IF AZURE_OPENAI
//var azureOpenAIEndpoint = new Uri(new Uri(builder.Configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("Missing configuration: AzureOpenAi:Endpoint. See the README for details.")), "/openai/v1");
//#pragma warning disable OPENAI001 // OpenAIClient(AuthenticationPolicy, OpenAIClientOptions) and GetOpenAIResponseClient(string) are experimental and subject to change or removal in future updates.
//var azureOpenAi = new OpenAIClient(
//    new ApiKeyCredential(builder.Configuration["AzureOpenAI:Key"] ?? throw new InvalidOperationException("Missing configuration: AzureOpenAi:Key. See the README for details.")),
//    new OpenAIClientOptions { Endpoint = azureOpenAIEndpoint });

//var chatClient = azureOpenAi.GetOpenAIResponseClient("gpt-4o-mini").AsIChatClient();
//#pragma warning restore OPENAI001
////## ENDIF

builder.Services.AddChatClient(chatClient);

var writer = builder.AddAIAgent("writer", "You write short stories (300 words or less) about the specific topic.");
var editor = builder.AddAIAgent("editor", "You edit short stories to improve grammar and style. You ensure the stories are less than 300 words.");

// ERROR: Invoking the workflow in DevUI results in a MissingMethodException error; likely just due to local build issues.
// builder.AddSequentialWorkflow("story-creation-workflow", [writer, editor]);

//// ERROR: The workflow factory returned workflow with name '', but the expected name is 'story-creation-workflow'.
//var workflow = builder.AddWorkflow("story-creation-workflow", (sp, name) =>
//{
//    var writer = sp.GetRequiredKeyedService<AIAgent>("writer");
//    var editor = sp.GetRequiredKeyedService<AIAgent>("editor");

//    var groupChatBuilder = AgentWorkflowBuilder.CreateGroupChatBuilderWith(
//        agents => new RoundRobinGroupChatManager(
//            agents,
//            (manager, messages, cancellationToken) => ValueTask.FromResult(messages.LastOrDefault()?.AuthorName == editor.Name)
//        )
//        { MaximumIterationCount = 4 }
//    );

//    return groupChatBuilder.AddParticipants(writer, editor).Build();
//});

//// ERROR: No keyed service for type 'Microsoft.Agents.AI.Workflows.Workflow' using key type 'System.String' has been registered.
//workflow.AddAsAIAgent("story-creation-agent");

// WORK AROUND THE INABILITY TO ADD A WORKFLOW AND THEN ADD IT AS AN AIAGENT
builder.AddAIAgent("publisher", (sp, name) =>
{
    var writer = sp.GetRequiredKeyedService<AIAgent>("writer");
    var editor = sp.GetRequiredKeyedService<AIAgent>("editor");

    var groupChatBuilder = AgentWorkflowBuilder.CreateGroupChatBuilderWith(
        agents => new RoundRobinGroupChatManager(
            agents,
            (manager, messages, cancellationToken) => ValueTask.FromResult(messages.LastOrDefault()?.AuthorName == editor.Name)
        )
        { MaximumIterationCount = 4 }
    );

    var groupChatWorkflow = groupChatBuilder
        .AddParticipants(writer, editor)
        .Build();

    return new WorkflowAgentWrapper(groupChatWorkflow.AsAgent(name: name));
});

builder.AddOpenAIResponses();
builder.AddDevUI();

var app = builder.Build();

app.MapOpenApi();
app.MapOpenAIResponses();
app.MapConversations();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapDevUI();
    app.MapEntities();
}

app.UseHttpsRedirection();
app.Run();

// WORK AROUND DEVUI FILTERING OUT THE WORKFLOW AGENT
class WorkflowAgentWrapper : DelegatingAIAgent
{
    public WorkflowAgentWrapper(AIAgent innerAgent) : base(innerAgent)
    {
    }
}
