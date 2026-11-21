using System.Numerics;
using Cherris.Core;
using Cherris.Core.Logging;
using MoonSharp.Interpreter;

namespace Cherris.Scripting;

/// <summary>Lua adapter. Each component owns a separate interpreter and globals.</summary>
public sealed class LuaScriptLanguage : IScriptLanguage
{
    public IScriptInstance CreateInstance(string source, string sourceName, GameObject gameObject)
        => new LuaInstance(source, sourceName, gameObject);

    private sealed class LuaInstance : IScriptInstance
    {
        private readonly MoonSharp.Interpreter.Script _script = new(CoreModules.Preset_HardSandbox);

        public LuaInstance(string source, string sourceName, GameObject gameObject)
        {
            _script.Globals.Set("log", DynValue.NewCallback((_, args) =>
            {
                Logger.Info($"[Lua:{gameObject.Name}] {args[0].ToPrintString()}");
                return DynValue.Nil;
            }));
            _script.Globals.Set("get_position", DynValue.NewCallback((_, _) =>
            {
                var position = gameObject.Transform.Position;
                return DynValue.NewTuple(DynValue.NewNumber(position.X), DynValue.NewNumber(position.Y), DynValue.NewNumber(position.Z));
            }));
            _script.Globals.Set("set_position", DynValue.NewCallback((_, args) =>
            {
                gameObject.Transform.Position = ReadVector(args);
                return DynValue.Nil;
            }));
            _script.Globals.Set("translate", DynValue.NewCallback((_, args) =>
            {
                gameObject.Transform.Position += ReadVector(args);
                return DynValue.Nil;
            }));
            _script.DoString(source, null, sourceName);
        }

        private static Vector3 ReadVector(CallbackArguments args)
        {
            if (args.Count != 3 || args[0].Type != DataType.Number || args[1].Type != DataType.Number || args[2].Type != DataType.Number)
                throw new ScriptRuntimeException("Expected three numeric coordinates.");
            return new Vector3((float)args[0].Number, (float)args[1].Number, (float)args[2].Number);
        }

        public void Start() => Call("start");
        public void Update(float deltaTime) => Call("update", DynValue.NewNumber(deltaTime));

        private void Call(string name, params DynValue[] args)
        {
            DynValue function = _script.Globals.Get(name);
            if (function.Type == DataType.Nil || function.Type == DataType.Void) return;
            if (function.Type != DataType.Function && function.Type != DataType.ClrFunction)
                throw new InvalidOperationException($"Lua global '{name}' must be a function.");
            _script.Call(function, args);
        }
    }
}
