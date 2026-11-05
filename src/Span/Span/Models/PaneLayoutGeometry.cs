using System;

namespace Span.Models;

public readonly record struct PaneBounds(double X, double Y, double Width, double Height);

public static class PaneLayoutGeometry
{
    public static double ClampSplitRatio(double available, double desired)
    {
        if (!double.IsFinite(available) || available <= 0)
            return 0.5;

        double minimum = Math.Min(150 / available, 0.5);
        return Math.Clamp(double.IsFinite(desired) ? desired : 0.5, minimum, 1 - minimum);
    }

    public static int WindowWidthForColumnDrag(int width, int delta, int left, int workRight, int minWidth)
    {
        long available = Math.Clamp((long)workRight - left, 0, int.MaxValue);
        long minimum = Math.Clamp((long)minWidth, 0, available);
        return (int)Math.Clamp((long)width + delta, minimum, available);
    }

    public static PaneBounds[] Tile(int count, int columns, double hostWidth, double hostHeight)
        => Tile(count, columns, hostWidth, hostHeight, Array.Empty<double>(), Array.Empty<double>());

    public static PaneBounds[] Tile(int count, int columns, double hostWidth, double hostHeight,
        double[] columnWeights, double[] rowWeights)
    {
        if (count <= 0)
            return Array.Empty<PaneBounds>();

        columns = Math.Clamp(columns, 1, count);
        int rows = (count - 1) / columns + 1;
        double[] columnStops = AxisStops(columns, ValidSize(hostWidth), columnWeights);
        double[] rowStops = AxisStops(rows, ValidSize(hostHeight), rowWeights);
        var bounds = new PaneBounds[count];
        for (int index = 0; index < count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            double x = columnStops[column];
            double y = rowStops[row];
            double right = columnStops[column + 1];
            double bottom = rowStops[row + 1];
            bounds[index] = new PaneBounds(x, y, right - x, bottom - y);
        }
        return bounds;
    }

    private static double[] AxisStops(int count, double available, double[] weights)
    {
        bool weighted = weights != null && weights.Length == count;
        double largest = 0;
        if (weighted)
        {
            foreach (double weight in weights!)
            {
                if (!double.IsFinite(weight) || weight <= 0)
                {
                    weighted = false;
                    break;
                }
                largest = Math.Max(largest, weight);
            }
        }

        double total = 0;
        if (weighted)
            foreach (double weight in weights!)
                total += weight / largest;

        var stops = new double[count + 1];
        double cumulative = 0;
        for (int index = 1; index < count; index++)
        {
            if (weighted)
                cumulative += weights![index - 1] / largest;
            stops[index] = available * (weighted ? cumulative / total : (double)index / count);
        }
        stops[count] = available;
        return stops;
    }

    public static PaneBounds ConstrainFloating(PaneBounds bounds, double hostWidth, double hostHeight)
    {
        double availableWidth = ValidSize(hostWidth);
        double availableHeight = ValidSize(hostHeight);
        double minimumWidth = Math.Min(150, availableWidth);
        double minimumHeight = Math.Min(120, availableHeight);
        double width = Math.Clamp(ValidSize(bounds.Width), minimumWidth, availableWidth);
        double height = Math.Clamp(ValidSize(bounds.Height), minimumHeight, availableHeight);
        double x = Math.Clamp(double.IsFinite(bounds.X) ? bounds.X : 0, 0, availableWidth - width);
        double y = Math.Clamp(double.IsFinite(bounds.Y) ? bounds.Y : 0, 0, availableHeight - height);
        return new PaneBounds(x, y, width, height);
    }

    private static double ValidSize(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
}
