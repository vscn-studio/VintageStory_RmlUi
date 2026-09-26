# RmlUi Theme Previews

Run the OpenGL integration test to regenerate screenshots from the actual RmlUi renderer:

```powershell
dotnet run --project tests/VSRmlUi.Tests -p:GameDirectory=E:/vintagestory/Vintagestory -- E:/vintagestory/VintageStory_RmlUi --game=E:/vintagestory/Vintagestory
```

The test writes PNG files under `artifacts/previews/`. This directory is generated output and is ignored by Git.

| Design | Previews |
| --- | --- |
| Workbench | `workbench-default.png`, `workbench-night.png`, `workbench-day.png`, `workbench-contrast.png` |
| Narrow workbench | `workbench-<variant>-narrow.png` for all four variants |
| Tabbed tool | `tabbed-<variant>-general.png`, `tabbed-<variant>-output.png`, `tabbed-<variant>-advanced.png` for all four variants |
| Narrow tabbed tool | `tabbed-default-narrow.png` |
| Color dialog | `color-<variant>.png` for all four variants |
| Folder dialog | `folder-<variant>.png` for all four variants |
| Input diagnostics | `input-diagnostics-<variant>.png` for all four variants |
| Basic modal | `modal-<variant>.png` for all four variants |
| Tabler SVG smoke test | `tabler-search.png` |

`<variant>` is `default`, `night`, `day`, or `contrast`. The folder previews use a static folder list fixture with the same RML structure and styles as the runtime dialog; no filesystem contents are included.
