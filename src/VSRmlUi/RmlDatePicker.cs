using System.Globalization;
using System.Text;

namespace VSRmlUi;

public sealed class RmlDatePicker(string id, DateOnly selected)
{
    private RmlDocument? document;
    private readonly List<IDisposable> handlers = [];
    private DateOnly month = new(selected.Year, selected.Month, 1);
    public DateOnly Selected { get; private set; } = selected;
    public DateOnly VisibleMonth => month;
    public event Action<DateOnly>? Changed;

    public string Markup()
        => $"<div id='{E(id)}' class='vs-date-picker'><div class='vs-date-head'><button id='{E(id)}-prev' aria-label='Previous month'>&lt;</button><strong id='{E(id)}-month'>{month:yyyy MMMM}</strong><button id='{E(id)}-next' aria-label='Next month'>&gt;</button></div><div class='vs-date-grid' id='{E(id)}-grid'>{Days()}</div></div>";

    public void Bind(RmlDocument page)
    {
        document = page;
        handlers.Add(page.GetElementById(id + "-prev")!.On("click", _ => MoveMonth(-1)));
        handlers.Add(page.GetElementById(id + "-next")!.On("click", _ => MoveMonth(1)));
        BindDays();
    }

    public void Unbind()
    {
        foreach (var handler in handlers) handler.Dispose();
        handlers.Clear(); document = null;
    }

    public void MoveMonth(int offset)
    {
        month = month.AddMonths(offset);
        if (document is null) return;
        document.GetElementById(id + "-month")!.Text = month.ToString("yyyy MMMM", CultureInfo.InvariantCulture);
        foreach (var handler in handlers.Skip(2).ToArray()) { handler.Dispose(); handlers.Remove(handler); }
        document.GetElementById(id + "-grid")!.InnerRml = Days();
        BindDays();
    }

    public void Select(DateOnly value)
    {
        Selected = value;
        if (value.Year != month.Year || value.Month != month.Month) { month = new(value.Year, value.Month, 1); MoveMonth(0); }
        else if (document is not null)
        {
            foreach (var day in document.Root.QuerySelectorAll(".vs-date-day"))
                day.SetClass("selected", day.GetAttribute("data-date") == value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        Changed?.Invoke(value);
    }

    private void BindDays()
    {
        if (document is null) return;
        foreach (var day in document.Root.QuerySelectorAll(".vs-date-day"))
            handlers.Add(day.On("click", _ => Select(DateOnly.ParseExact(day.GetAttribute("data-date"), "yyyy-MM-dd", CultureInfo.InvariantCulture))));
    }

    private string Days()
    {
        var html = new StringBuilder();
        foreach (string label in new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }) html.Append("<span class='vs-date-weekday'>").Append(label).Append("</span>");
        int start = ((int)month.DayOfWeek + 6) % 7;
        for (int i = 0; i < start; i++) html.Append("<span class='vs-date-empty'/>");
        for (int day = 1; day <= DateTime.DaysInMonth(month.Year, month.Month); day++)
        {
            var date = new DateOnly(month.Year, month.Month, day);
            html.Append($"<button class='vs-date-day{(date == Selected ? " selected" : "")}' data-date='{date:yyyy-MM-dd}'>{day}</button>");
        }
        return html.ToString();
    }

    private static string E(string text) => System.Net.WebUtility.HtmlEncode(text);
}
