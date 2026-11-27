# Project templates and samples

The project launchers offer a small built-in library of projects. Pick **Blank project**, **3D starter**, or **UI showcase** when creating a project.

- **Blank project** creates the same clean workspace as before, with an empty start scene.
- **3D starter** opens a lit built-in cube scene. It has no imported assets or network dependencies.
- **UI showcase** opens a responsive Canvas with an interactive button, a keyboard-editable input field, and Arabic/Persian text to exercise the bundled font fallback and RTL layout.

Creating from a sample copies its files into a new folder and changes the copied `project.yaml` to the name you entered. The bundled source stays untouched. If the destination already exists, creation fails without modifying it. Each copied sample includes a README with a few things to try.

Template files live under `src/Overcore/Resources/ProjectTemplates/` and are copied with the engine resources into application output. Keep templates self-contained: rely on built-in engine assets where possible, include licenses for any bundled third-party assets, and make sure the project's declared start scene exists. New templates should be added to `ProjectTemplateCatalog` and covered by a copy/load test.
