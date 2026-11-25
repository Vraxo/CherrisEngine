namespace OvercoreEditor;

public static class ScriptTemplate
{
    public static string GetContent(string scriptName)
    {
        return $@"using Overcore.Components;
using System.Numerics;

public class {scriptName} : Script
{{
    // Called when the script instance is being loaded
    public override void Start()
    {{
        
    }}

    // Called every frame
    public override void Update(float deltaTime)
    {{
        
    }}
}}
";
    }
}