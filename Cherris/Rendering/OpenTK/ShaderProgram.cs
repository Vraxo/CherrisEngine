using Cherris.Core.Logging;
using Cherris.Utils;
using OpenTK.Graphics.OpenGL;

public class ShaderProgram : IDisposable
{
    public readonly int Handle;
    private bool _disposed;

    public ShaderProgram(string vertexSource, string fragmentSource)
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertexShader);
        GL.AttachShader(Handle, fragmentShader);
        GL.LinkProgram(Handle);

        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            var info = GL.GetProgramInfoLog(Handle);
            throw new InvalidOperationException($"Program linking failed: {info}");
        }

        GL.DetachShader(Handle, vertexShader);
        GL.DetachShader(Handle, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    public static ShaderProgram? FromFiles(string vertexPath, string fragmentPath)
    {
        var vertSourcePath = EditorResources.Find(vertexPath);
        var fragSourcePath = EditorResources.Find(fragmentPath);

        if (vertSourcePath is null || fragSourcePath is null)
        {
            Logger.Error($"[ShaderProgram] Could not find shader files: {vertexPath}, {fragmentPath}");
            return null;
        }

        try
        {
            var vertSource = File.ReadAllText(vertSourcePath);
            var fragSource = File.ReadAllText(fragSourcePath);
            return new ShaderProgram(vertSource, fragSource);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ShaderProgram] {ex.Message}");
            return null;
        }
    }


    private static int CompileShader(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            var info = GL.GetShaderInfoLog(shader);
            throw new InvalidOperationException($"{type} compilation failed: {info}");
        }
        return shader;
    }

    public void Use()
    {
        GL.UseProgram(Handle);
    }

    public int GetUniformLocation(string name)
    {
        return GL.GetUniformLocation(Handle, name);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteProgram(Handle);
            _disposed = true;
        }
    }
}