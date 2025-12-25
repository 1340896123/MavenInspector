using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.ComponentModel;

namespace MavenInspector;

public class MavenInspectorPrompts
{
    [McpServerPrompt(Name = "analyze-project")]
    [Description("Guides the user to analyze a Java project by providing a pom.xml path")]
    public ChatMessage[] AnalyzeProjectPrompt(
        [Description("The absolute path to the project's pom.xml")] string pomPath)
    {
        return
            new[] {

                new ChatMessage(ChatRole.User, $"Please analyze the dependencies for the project at {pomPath} and search for any critical classes.")
    };
    }
}
