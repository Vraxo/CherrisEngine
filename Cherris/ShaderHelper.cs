using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Cherris;

public static class ShaderHelper
{
    private const string CachePath = "Shaders/Cache";

    static ShaderHelper()
    {
        Directory.CreateDirectory(CachePath);
    }

    public static Shader[] LoadFromGlsl(
        ResourceFactory factory,
        string vertexCode,
        string fragmentCode)
    {
        var sw = Stopwatch.StartNew();
        string combinedCode = vertexCode + fragmentCode;
        string hash = GetHash(combinedCode);
        string vertCachePath = Path.Combine(CachePath, $"{hash}.vert.spv");
        string fragCachePath = Path.Combine(CachePath, $"{hash}.frag.spv");

        byte[] vertSpirvBytes;
        byte[] fragSpirvBytes;

        if (File.Exists(vertCachePath) && File.Exists(fragCachePath))
        {
            // Load from cache
            vertSpirvBytes = File.ReadAllBytes(vertCachePath);
            fragSpirvBytes = File.ReadAllBytes(fragCachePath);
            sw.Stop();
            Console.WriteLine($"[PROFILE] Loaded shader from cache '{hash}' in {sw.ElapsedMilliseconds}ms");
        }
        else
        {
            // Compile and save to cache
            Console.WriteLine($"[PROFILE] Compiling shader '{hash}'...");
            var options = new GlslCompileOptions { Debug = false };

            SpirvCompilationResult vertResult = SpirvCompilation.CompileGlslToSpirv(vertexCode, "main", ShaderStages.Vertex, options);
            vertSpirvBytes = vertResult.SpirvBytes;

            SpirvCompilationResult fragResult = SpirvCompilation.CompileGlslToSpirv(fragmentCode, "main", ShaderStages.Fragment, options);
            fragSpirvBytes = fragResult.SpirvBytes;

            File.WriteAllBytes(vertCachePath, vertSpirvBytes);
            File.WriteAllBytes(fragCachePath, fragSpirvBytes);
            sw.Stop();
            Console.WriteLine($"[PROFILE] Shader compilation and caching for '{hash}' took {sw.ElapsedMilliseconds}ms");
        }

        ShaderDescription vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, vertSpirvBytes, "main");
        ShaderDescription fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, fragSpirvBytes, "main");

        return factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
    }

    private static string GetHash(string text)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}