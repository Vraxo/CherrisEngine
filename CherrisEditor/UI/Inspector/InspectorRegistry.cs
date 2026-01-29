using Cherris.Core.Logging;
using CherrisEditor.Inspectors;
using CherrisEditor.Undo;
using System.Reflection;

namespace CherrisEditor.UI.Inspector;

public class InspectorRegistry
{
    private readonly Dictionary<Type, IComponentInspector> _customInspectors = [];

    public InspectorRegistry(
        Editor editor,
        EditorTextureManager textureManager,
        HistoryManager history)
    {
        RegisterCustomInspectors(editor, textureManager, history);
    }

    public IComponentInspector? GetInspector(Type componentType)
    {
        _customInspectors.TryGetValue(componentType, out var inspector);
        return inspector;
    }

    private void RegisterCustomInspectors(
        Editor editor,
        EditorTextureManager textureManager,
        HistoryManager history)
    {
        var inspectorTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsDefined(typeof(CustomInspectorAttribute), false) &&
                        typeof(IComponentInspector).IsAssignableFrom(t));

        foreach (var inspectorType in inspectorTypes)
        {
            var attribute = (CustomInspectorAttribute)inspectorType
                .GetCustomAttribute(typeof(CustomInspectorAttribute), false)!;

            try
            {
                IComponentInspector? instance = CreateInspectorInstance(
                    inspectorType, editor, textureManager, history);

                if (instance is not null)
                {
                    _customInspectors[attribute.InspectedType] = instance;
                    Logger.Info($"[Inspector] Registered custom inspector for '{attribute.InspectedType.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[Inspector] Failed to register custom inspector '{inspectorType.Name}': {ex.Message}");
            }
        }
    }

    private static IComponentInspector? CreateInspectorInstance(
        Type inspectorType,
        Editor editor,
        EditorTextureManager textureManager,
        HistoryManager history)
    {
        var ctorWithAll = inspectorType.GetConstructor(new[] { typeof(Editor), typeof(EditorTextureManager), typeof(HistoryManager) });
        if (ctorWithAll is not null)
        {
            return (IComponentInspector)Activator.CreateInstance(inspectorType, editor, textureManager, history)!;
        }

        var ctorWithEditor = inspectorType.GetConstructor(new[] { typeof(Editor), typeof(EditorTextureManager) });
        if (ctorWithEditor is not null)
        {
            return (IComponentInspector)Activator.CreateInstance(inspectorType, editor, textureManager)!;
        }

        var ctorWithTextureAndHistory = inspectorType.GetConstructor(new[] { typeof(EditorTextureManager), typeof(HistoryManager) });
        if (ctorWithTextureAndHistory is not null)
        {
            return (IComponentInspector)Activator.CreateInstance(inspectorType, textureManager, history)!;
        }

        var ctorWithHistoryOnly = inspectorType.GetConstructor(new[] { typeof(HistoryManager) });
        if (ctorWithHistoryOnly is not null)
        {
            return (IComponentInspector)Activator.CreateInstance(inspectorType, history)!;
        }

        var ctorWithTextureOnly = inspectorType.GetConstructor(new[] { typeof(EditorTextureManager) });
        if (ctorWithTextureOnly is not null)
        {
            return (IComponentInspector)Activator.CreateInstance(inspectorType, textureManager)!;
        }

        var ctorParameterless = inspectorType.GetConstructor(Type.EmptyTypes);
        if (ctorParameterless is not null)
        {
            return (IComponentInspector)Activator.CreateInstance(inspectorType)!;
        }

        Logger.Warning($"[Inspector] Could not find a suitable constructor for '{inspectorType.Name}'.");
        return null;
    }
}