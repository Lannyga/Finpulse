using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace Finpulse.Desktop.Controls;

public sealed record ChartValue(string Label, decimal Value);
public sealed record ChartSeries(string Name, string BrushKey, int LineStyle, IReadOnlyList<ChartValue> Values);

/// <summary>Bounded, native vector chart. Money stays decimal; double is used only for drawing coordinates.</summary>
public sealed class FinancialLineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(nameof(Series), typeof(IReadOnlyList<ChartSeries>),
        typeof(FinancialLineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ResetSelection));
    public IReadOnlyList<ChartSeries>? Series { get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    private int _selected;
    public string SelectedText { get; private set; } = "Sin datos";
    public FinancialLineChart() { Focusable = true; MinHeight = 250; ClipToBounds = true; }
    private static void ResetSelection(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    { var chart = (FinancialLineChart)obj; chart._selected = 0; chart.UpdateDescription(); }
    private Brush Brush(string key) => TryFindResource(key) as Brush ?? System.Windows.Media.Brushes.White;
    private void Text(DrawingContext dc, string text, double x, double y, string brush = "MutedBrush", double size = 12)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, Brush(brush), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new(x, y));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brush("PanelBrush"), null, new Rect(RenderSize));
        var series = Series?.Where(s => s.Values.Count > 0).ToArray() ?? [];
        if (series.Length == 0) { Text(dc, "Sin datos para graficar. Registra movimientos o introduce supuestos.", 16, 90); return; }
        int legendsPerRow = Math.Max(1, (int)(ActualWidth / 180));
        int legendRows = (int)Math.Ceiling((double)series.Length / legendsPerRow);
        double left = 94, right = Math.Max(110, ActualWidth - 24), top = 58 + (legendRows - 1) * 24, bottom = Math.Max(120, ActualHeight - 68);
        decimal min = Math.Min(0, series.Min(s => s.Values.Min(p => p.Value))), max = Math.Max(0, series.Max(s => s.Values.Max(p => p.Value)));
        if (min == max) max = min + 1;
        int count = series.Max(s => s.Values.Count);
        double X(int i) => left + (right - left) * i / Math.Max(1, count - 1);
        double Y(decimal value) => bottom - (double)((value - min) / (max - min)) * (bottom - top);
        var gridPen = new Pen(Brush("BorderBrush"), .5); gridPen.Freeze();
        for (int tick = 0; tick <= 4; tick++)
        {
            decimal value = min + (max - min) * tick / 4;
            double y = Y(value); dc.DrawLine(gridPen, new(left, y), new(right, y));
            Text(dc, value.ToString(Math.Abs(value) >= 1_000_000 ? "0.##E+0" : "N0", CultureInfo.CurrentCulture), 4, y - 8);
        }
        Text(dc, "Bs", 4, 30);
        int legendIndex = 0;
        foreach (var s in series)
        {
            var pen = new Pen(Brush(s.BrushKey), 2.5) { DashStyle = s.LineStyle == 1 ? DashStyles.Dash : s.LineStyle == 2 ? DashStyles.Dot : DashStyles.Solid }; pen.Freeze();
            double legendX = 12 + legendIndex % legendsPerRow * 180, legendY = 16 + legendIndex / legendsPerRow * 24;
            dc.DrawLine(pen, new(legendX, legendY), new(legendX + 24, legendY)); Text(dc, s.Name, legendX + 30, legendY - 10, "TextBrush");
            legendIndex++;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new(X(0), Y(s.Values[0].Value)), false, false);
                for (int i = 1; i < s.Values.Count; i++) context.LineTo(new(X(i), Y(s.Values[i].Value)), true, false);
            }
            geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
            int selected = Math.Min(_selected, s.Values.Count - 1);
            dc.DrawEllipse(Brush(s.BrushKey), null, new(X(selected), Y(s.Values[selected].Value)), 4, 4);
        }
        var labels = series.OrderByDescending(s => s.Values.Count).First().Values;
        int step = Math.Max(1, (int)Math.Ceiling((count - 1) / Math.Max(1, (right - left) / 115)));
        for (int i = 0; i < count; i += step) Text(dc, labels[i].Label, X(i) - 18, bottom + 10);
        Text(dc, SelectedText, 12, ActualHeight - 25, "TextBrush", 11);
        if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(Brush("AccentBrush"), 2), new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int count = Series?.Select(s => s.Values.Count).DefaultIfEmpty(0).Max() ?? 0;
        if (count == 0) return;
        int selected = Math.Clamp((int)Math.Round((e.GetPosition(this).X - 94) / Math.Max(1, ActualWidth - 118) * (count - 1)), 0, count - 1);
        if (selected != _selected) SelectPoint(selected);
    }
    protected override void OnMouseDown(MouseButtonEventArgs e) { base.OnMouseDown(e); Focus(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int count = Series?.Select(s => s.Values.Count).DefaultIfEmpty(0).Max() ?? 0;
        if (count == 0) return;
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            SelectPoint(e.Key == Key.Home ? 0 : e.Key == Key.End ? count - 1 : _selected + (e.Key == Key.Right ? 1 : -1));
            e.Handled = true;
        }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    public void SelectPoint(int index)
    {
        int count = Series?.Select(s => s.Values.Count).DefaultIfEmpty(0).Max() ?? 0;
        _selected = Math.Clamp(index, 0, Math.Max(0, count - 1)); UpdateDescription(); InvalidateVisual();
    }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    private void UpdateDescription()
    {
        var active = Series?.Where(s => s.Values.Count > 0).ToArray() ?? [];
        SelectedText = active.Length == 0 ? "Sin datos" : active[0].Values[Math.Min(_selected, active[0].Values.Count - 1)].Label + " · " +
            string.Join(" · ", active.Select(s => $"{s.Name}: Bs {s.Values[Math.Min(_selected, s.Values.Count - 1)].Value:N2}"));
        ToolTip = SelectedText + "\nTeclado: flechas, Inicio y Fin. Los valores también están disponibles en la tabla.";
        AutomationProperties.SetHelpText(this, SelectedText);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
