namespace Span.Models
{
    public enum PaneLayoutKind { Classic, Tiled, Floating }
    public enum PaneTileArrangement { Horizontal, Vertical, Grid }

    public sealed class WorkspacePaneState
    {
        public string Id { get; } = System.Guid.NewGuid().ToString("N");
        public ViewModels.ExplorerViewModel Explorer { get; }
        public ViewMode Mode { get; set; }
        public ViewMode PreferredViewMode { get; set; } = ViewMode.MillerColumns;
        public PaneBounds Bounds { get; set; } = new PaneBounds(24, 24, 420, 300);
        public WorkspacePaneState(ViewModels.ExplorerViewModel explorer, ViewMode mode)
        {
            Explorer = explorer;
            Mode = mode;
        }
    }

    public sealed class WorkspaceLayoutState
    {
        public System.Collections.Generic.List<WorkspacePaneState> Panes { get; } = new();
        public WorkspacePaneState? ActivePane { get; private set; }
        public bool Enabled { get; set; }
        public bool ClassicSplitEnabled { get; set; }
        public PaneLayoutKind Layout { get; set; } = PaneLayoutKind.Tiled;
        public PaneTileArrangement Arrangement { get; set; } = PaneTileArrangement.Grid;
        public double[] ColumnWeights { get; set; } = System.Array.Empty<double>();
        public double[] RowWeights { get; set; } = System.Array.Empty<double>();
        public WorkspacePaneState Add(ViewModels.ExplorerViewModel explorer, ViewMode mode)
        {
            var pane = new WorkspacePaneState(explorer, mode);
            Panes.Add(pane);
            ActivePane = pane;
            return pane;
        }
        public void Activate(WorkspacePaneState pane)
        {
            if (Panes.Contains(pane)) ActivePane = pane;
        }
        public bool Remove(WorkspacePaneState pane)
        {
            if (Panes.Count <= 1 || !Panes.Remove(pane)) return false;
            if (ActivePane == pane) ActivePane = Panes[0];
            return true;
        }
        public void MoveBefore(WorkspacePaneState pane, WorkspacePaneState target)
        {
            if (pane == target || !Panes.Contains(pane) || !Panes.Contains(target)) return;
            Panes.Remove(pane);
            Panes.Insert(Panes.IndexOf(target), pane);
        }
    }
}
