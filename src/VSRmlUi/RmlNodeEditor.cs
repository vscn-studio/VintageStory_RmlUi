using System.Globalization;
using System.Text;

namespace VSRmlUi;

public sealed record RmlNodePort(string Id, string Label, string Color);
public sealed record RmlNode(string Id, string Title, float X, float Y, IReadOnlyList<RmlNodePort> Inputs, IReadOnlyList<RmlNodePort> Outputs);
public sealed record RmlNodeLink(string FromNode, string FromPort, string ToNode, string ToPort);

/// <summary>Retained node graph with draggable nodes and color-coded Bezier links.</summary>
public sealed class RmlNodeEditor
{
    private readonly Dictionary<string, RmlNode> nodes = new(StringComparer.Ordinal);
    private readonly List<RmlNodeLink> links = [];
    private RmlDocument? document;
    private string? dragNode;
    private float dragX, dragY;
    private readonly List<IDisposable> subscriptions = [];
    public string Id { get; }
    public RmlNodeEditor(string id) => Id = id;
    public IReadOnlyCollection<RmlNode> Nodes => nodes.Values;
    public IReadOnlyList<RmlNodeLink> Links => links;

    public void AddNode(RmlNode node)
    {
        if (!nodes.TryAdd(node.Id, node)) throw new ArgumentException("Duplicate node id.", nameof(node));
        Refresh();
    }

    public void RemoveNode(string id)
    {
        if (!nodes.Remove(id)) return;
        links.RemoveAll(link => link.FromNode == id || link.ToNode == id);
        Refresh();
    }

    public void MoveNode(string id, float x, float y)
    {
        if (!nodes.TryGetValue(id, out var node)) throw new ArgumentException("Unknown node.", nameof(id));
        nodes[id] = node with { X = Math.Clamp(x, 0, 728), Y = Math.Clamp(y, 0, 430) };
        if (document is not null)
        {
            var element = document.GetElementById(NodeId(id));
            element?.SetProperty("left", N(nodes[id].X) + "dp");
            element?.SetProperty("top", N(nodes[id].Y) + "dp");
            UpdateLinks();
        }
    }

    public void Connect(RmlNodeLink link)
    {
        if (!nodes.TryGetValue(link.FromNode, out var source) || !nodes.TryGetValue(link.ToNode, out var target)
            || source.Outputs.All(port => port.Id != link.FromPort) || target.Inputs.All(port => port.Id != link.ToPort))
            throw new ArgumentException("The link must connect an output to an input.", nameof(link));
        if (links.Contains(link)) return;
        links.Add(link);
        UpdateLinks();
    }

    public void Disconnect(RmlNodeLink link) { if (links.Remove(link)) UpdateLinks(); }

    public string Markup()
    {
        return $"<div id='{E(Id)}' class='vs-node-editor'>" + Contents() + "</div>";
    }

    private string Contents()
    {
        var html = new StringBuilder($"<svg id='{E(Id)}-wires' class='vs-node-wires' width='960' height='560' viewBox='0 0 960 560'/>");
        foreach (var node in nodes.Values) html.Append(NodeMarkup(node));
        return html.ToString();
    }

    public void Bind(RmlDocument page)
    {
        if (document is not null) Unbind();
        document = page;
        page.Closed += StopDrag;
        page.InputCancelled += StopDrag;
        foreach (var node in nodes.Values)
        {
            var card = page.GetElementById(NodeId(node.Id))!;
            subscriptions.Add(card.On("mousedown", e =>
            {
                if (e.Button != 0) return;
                dragNode = node.Id;
                var bounds = page.GetElementById(Id)!.Bounds;
                dragX = e.MouseX - bounds.X - nodes[node.Id].X;
                dragY = e.MouseY - bounds.Y - nodes[node.Id].Y;
            }));
            subscriptions.Add(card.On("drag", e =>
            {
                if (dragNode == node.Id)
                {
                    var bounds = page.GetElementById(Id)!.Bounds;
                    MoveNode(node.Id, e.MouseX - bounds.X - dragX, e.MouseY - bounds.Y - dragY);
                }
            }));
            subscriptions.Add(card.On("dragend", _ => StopDrag()));
        }
        UpdateLinks();
    }

    public void Unbind()
    {
        if (document is not null && !document.IsDisposed)
        {
            document.Closed -= StopDrag;
            document.InputCancelled -= StopDrag;
        }
        foreach (var subscription in subscriptions) subscription.Dispose();
        subscriptions.Clear(); document = null; dragNode = null;
    }

    private void StopDrag() => dragNode = null;
    private void Refresh()
    {
        if (document is null) return;
        var page = document;
        Unbind();
        page.GetElementById(Id)!.InnerRml = Contents();
        Bind(page);
    }

    private string NodeMarkup(RmlNode node)
    {
        var html = new StringBuilder($"<div id='{E(NodeId(node.Id))}' class='vs-node' style='left:{N(node.X)}dp; top:{N(node.Y)}dp'><div class='vs-node-title'>{E(node.Title)}</div><div class='vs-node-ports'>");
        int rows = Math.Max(node.Inputs.Count, node.Outputs.Count);
        for (int i = 0; i < rows; i++)
        {
            html.Append("<div class='vs-node-port-row'>");
            if (i < node.Inputs.Count) html.Append(PortMarkup(node.Inputs[i], "input")); else html.Append("<span/>");
            if (i < node.Outputs.Count) html.Append(PortMarkup(node.Outputs[i], "output"));
            html.Append("</div>");
        }
        return html.Append("</div></div>").ToString();
    }

    private static string PortMarkup(RmlNodePort port, string direction)
        => $"<div class='vs-node-port {direction}'><span class='vs-node-dot' style='background-color:{E(port.Color)}'/><span>{E(port.Label)}</span></div>";

    private void UpdateLinks()
    {
        if (document is null) return;
        var svg = new StringBuilder();
        foreach (var link in links)
        {
            var from = nodes[link.FromNode]; var to = nodes[link.ToNode];
            int fromRow = from.Outputs.ToList().FindIndex(port => port.Id == link.FromPort);
            int toRow = to.Inputs.ToList().FindIndex(port => port.Id == link.ToPort);
            float x1 = from.X + 230, y1 = from.Y + 46 + fromRow * 31;
            float x2 = to.X, y2 = to.Y + 46 + toRow * 31;
            string color = from.Outputs[fromRow].Color;
            float bend = Math.Max(55, Math.Abs(x2 - x1) * 0.5f);
            svg.Append($"<path d='M{N(x1)} {N(y1)} C{N(x1 + bend)} {N(y1)} {N(x2 - bend)} {N(y2)} {N(x2)} {N(y2)}' fill='none' stroke='{E(color)}' stroke-width='3'/>");
            svg.Append($"<circle cx='{N(x1)}' cy='{N(y1)}' r='5' fill='{E(color)}'/><circle cx='{N(x2)}' cy='{N(y2)}' r='5' fill='{E(color)}'/>");
        }
        document.GetElementById(Id + "-wires")!.InnerRml = svg.ToString();
    }

    private string NodeId(string nodeId) => Id + "-node-" + nodeId;
    private static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
