using System.Net;
using Vintagestory.API.Client;

namespace VSRmlUi;

/// <summary>Local folder selection in RmlUi. Disk enumeration runs off the game thread.</summary>
public sealed class RmlFolderDialog : IDisposable
{
    private readonly RmlDocument parent;
    private readonly ICoreClientAPI api;
    private readonly Action<string> accepted;
    private int generation;
    private bool disposed, busy;
    private string current = "";
    public RmlDocument Document { get; }
    public static RmlFolderDialog Show(RmlDocument parent, ICoreClientAPI api, string initialPath, Action<string> accepted)
        => new(parent, api, initialPath, accepted);
    private RmlFolderDialog(RmlDocument parent, ICoreClientAPI api, string initialPath, Action<string> accepted)
    {
        parent.EnsureAlive(); this.parent = parent; this.api = api; this.accepted = accepted;
        Document = parent.Runtime.LoadDocumentFromString(parent.OwnerModId,
            "<rml><head><link type='text/rcss' href='vsrmlui:dialog/folder-dialog.rcss'/><link type='text/rcss' href='vsrmlui:dialog/dialog-theme.rcss'/></head><body>"
            + "<div id='folder-dialog' role='dialog'><div class='folder-title'>[[vsrmlui:folder-title]]</div>"
            + "<div class='folder-address'><button id='up'>[[vsrmlui:folder-up]]</button><input id='address' type='text'/><button id='go'>[[vsrmlui:folder-go]]</button></div>"
            + "<div class='folder-main'><div class='folder-sidebar' id='locations'></div><div class='folder-list' id='folders'></div></div>"
            + "<div id='message'></div><div class='folder-footer'><button id='cancel'>[[vsrmlui:folder-cancel]]</button><button id='choose'>[[vsrmlui:folder-select]]</button></div>"
            + "</div></body></rml>", "vsrmlui:dialog/folder-dialog.rml",
            new() { Mode = RmlWindowMode.Modal, DrawOrder = Math.Max(.95, parent.Options.DrawOrder + .01), InputOrder = Math.Min(-.3, parent.Options.InputOrder - .1) });
        foreach (string variant in new[] { "night", "day", "contrast" })
        {
            string name = "vs-theme-" + variant;
            Get("folder-dialog").SetClass(name, parent.Root.ClassNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name) || parent.QuerySelector("." + name) is not null);
        }
        Document.Closed += Dispose; parent.Closed += Dispose;
        Get("cancel").On("click", _ => Dispose());
        Get("go").On("click", _ => Navigate(Get("address").Value));
        Get("address").On("keydown", e => { if (e.Key == (int)GlKeys.Enter) Navigate(Get("address").Value); });
        Get("up").On("click", _ => { if (current.Length > 0) Navigate(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current)) ?? current); });
        Get("choose").On("click", _ =>
        {
            if (busy || current.Length == 0) return;
            string value = current; Dispose(); if (!parent.IsDisposed) accepted(value);
        });
        Document.InputFilter = e => { if (e.Kind != RmlInputKind.KeyDown || e.Key != (int)GlKeys.Escape) return false; Dispose(); return true; };
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddLocation("[[vsrmlui:folder-home]]", user);
        foreach (string drive in Environment.GetLogicalDrives()) AddLocation(drive, drive);
        Document.Show(); Navigate(string.IsNullOrWhiteSpace(initialPath) ? user : initialPath);
    }
    private RmlElement Get(string id) => Document.GetElementById(id)!;
    private void AddLocation(string label, string path)
    {
        if (path.Length == 0) return;
        var button = Get("locations").AppendChild("button"); button.InnerRml = WebUtility.HtmlEncode(label);
        button.On("click", _ => Navigate(path));
    }
    private void Navigate(string path)
    {
        int request = ++generation; busy = true;
        Get("choose").SetAttribute("disabled", "disabled"); Get("message").InnerRml = "[[vsrmlui:folder-loading]]";
        _ = Task.Run(() =>
        {
            try
            {
                string full = Path.GetFullPath(path.Trim().Trim('"'));
                // EnumerateDirectories throws for missing/inaccessible directories, rather than silently accepting them.
                var folders = Directory.EnumerateDirectories(full).Take(2001).ToArray();
                return (Path: full, Folders: folders.Take(2000).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(), Truncated: folders.Length > 2000, Error: "");
            }
            catch (Exception e) { return (Path: "", Folders: Array.Empty<string>(), Truncated: false, Error: e.Message); }
        }).ContinueWith(task => api.Event.EnqueueMainThreadTask(() =>
        {
            if (disposed || request != generation || parent.IsDisposed) return;
            busy = false; var result = task.GetAwaiter().GetResult();
            if (result.Error.Length > 0)
            {
                Get("message").Text = result.Error;
                return;
            }
            current = result.Path; Get("address").Value = current; Get("choose").RemoveAttribute("disabled");
            Get("folders").InnerRml = "";
            foreach (string folder in result.Folders)
            {
                var button = Get("folders").AppendChild("button"); button.Text = Path.GetFileName(folder); button.SetAttribute("title", folder);
                button.On("click", _ => Navigate(folder));
            }
            Get("message").InnerRml = result.Truncated ? "[[vsrmlui:folder-truncated]]" : result.Folders.Length == 0 ? "[[vsrmlui:folder-empty]]" : "";
            Get("folders").SetScrollOffset(0, 0);
        }, "vsrmlui-folder-list"), TaskScheduler.Default);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; generation++; parent.Closed -= Dispose; Document.Dispose();
        if (!parent.IsDisposed && parent.IsVisible && parent.Host is GameDialog view) view.RestoreFocus();
    }
}
