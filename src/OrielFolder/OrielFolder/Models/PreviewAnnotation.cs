using System.Collections.Generic;

namespace OrielFolder.Models;

/// <summary>Coordinates are relative to the displayed page, with a top-left origin.</summary>
public sealed class PreviewAnnotation
{
    public string Tool { get; set; } = "pen";
    public string Color { get; set; } = "#e53935";
    public double Width { get; set; } = 0.004;
    // Null preserves the opacity of annotations made before this setting existed.
    public double? Opacity { get; set; }
    public List<AnnotationPoint> Points { get; set; } = new();
    public string Text { get; set; } = "";
    public int PageIndex { get; set; }
}

public sealed class AnnotationPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
