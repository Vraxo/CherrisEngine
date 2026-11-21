using Cherris.Core.Logging;
using Cherris.Utils;

namespace CherrisEditor.Core;

public class ProjectManager
{
    public Project? CurrentProject { get; private set; }

    public void LoadProject(string projectRoot)
    {
        if (!Directory.Exists(projectRoot))
        {
            Logger.Error($"[Editor] Project directory not found: {projectRoot}");
            return;
        }

        CurrentProject = Project.Load(projectRoot);
        ProjectFiles.ProjectRoot = projectRoot;
        ProjectPersistence.SetLastProject(projectRoot);

        Logger.Info($"[Editor] Loaded project: {CurrentProject.Name}");
    }
}