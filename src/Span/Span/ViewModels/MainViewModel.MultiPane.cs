using System.Collections.Generic;
using System.Linq;
using Span.Models;

namespace Span.ViewModels
{
    public partial class MainViewModel
    {
        public bool IsFreeLayoutActive { get; private set; }

        public ExplorerViewModel CreateWorkspaceExplorer(string path)
        {
            var explorer = new ExplorerViewModel(new FolderItem { Name = "PC", Path = "PC" }, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.MillerColumns);
            if (!string.IsNullOrWhiteSpace(path) && path != "PC")
                _ = explorer.NavigateToPath(path);
            return explorer;
        }

        public void ActivateWorkspacePane(WorkspacePaneState pane)
        {
            var state = ActiveTab?.WorkspaceLayout;
            if (state == null || !state.Panes.Contains(pane)) return;

            SaveWorkspacePaneMode();
            state.Activate(pane);
            pane.Mode = WorkspacePaneMode(pane.Mode);
            if (pane.Mode != ViewMode.Home) pane.PreferredViewMode = pane.Mode;

            // Property notifications can synchronously save tab state, so establish the bridge first.
            IsFreeLayoutActive = true;
            _activePane = ActivePane.Left;
            _isSplitViewEnabled = false;
            _currentViewMode = _leftViewMode = pane.Mode;
            _currentIconSize = Helpers.ViewModeExtensions.IsIconMode(pane.Mode) ? pane.Mode : ViewMode.IconMedium;
            pane.Explorer.EnableAutoNavigation = ShouldAutoNavigate(pane.Mode);
            LeftExplorer = pane.Explorer;
            OnPropertyChanged(nameof(IsFreeLayoutActive));
            OnPropertyChanged(nameof(CurrentIconSize));
            OnPropertyChanged(nameof(LeftViewMode));
            NotifyViewModeChanged();
            NotifySplitViewChanged();
            UpdateStatusBar();
            SyncNavigationHistoryState();
        }

        public void LeaveWorkspacePaneBridge()
        {
            if (!IsFreeLayoutActive) return;
            var tab = ActiveTab;
            SaveWorkspacePaneMode();
            IsFreeLayoutActive = false;
            if (tab == null) return;

            _currentViewMode = _leftViewMode = tab.ViewMode;
            _currentIconSize = tab.IconSize;
            _isSplitViewEnabled = tab.WorkspaceLayout?.ClassicSplitEnabled ?? tab.IsSplitEnabled;
            _rightViewMode = tab.SplitRightViewMode;
            _rightPreferredViewMode = tab.SplitRightPreferredViewMode;
            _activePane = _isSplitViewEnabled ? tab.SplitActivePane : ActivePane.Left;
            if (tab.Explorer != null)
            {
                tab.Explorer.EnableAutoNavigation = ShouldAutoNavigate(tab.ViewMode);
                LeftExplorer = tab.Explorer;
            }
            if (tab.SplitRightExplorer != null)
                RightExplorer = tab.SplitRightExplorer;
            OnPropertyChanged(nameof(IsFreeLayoutActive));
            OnPropertyChanged(nameof(CurrentIconSize));
            OnPropertyChanged(nameof(LeftViewMode));
            NotifyViewModeChanged();
            NotifySplitViewChanged();
            UpdateStatusBar();
            SyncNavigationHistoryState();
        }

        private static ViewMode WorkspaceFolderMode(ViewMode mode)
            => mode == ViewMode.MillerColumns || mode == ViewMode.Details || mode == ViewMode.List
                || Helpers.ViewModeExtensions.IsIconMode(mode) ? mode : ViewMode.MillerColumns;

        private static ViewMode WorkspacePaneMode(ViewMode mode)
            => mode == ViewMode.Home ? mode : WorkspaceFolderMode(mode);

        private void SaveWorkspacePaneMode()
        {
            if (!IsFreeLayoutActive) return;
            var pane = ActiveTab?.WorkspaceLayout?.Panes.FirstOrDefault(p => ReferenceEquals(p.Explorer, _leftExplorer));
            if (pane != null)
            {
                pane.Mode = WorkspacePaneMode(CurrentViewMode);
                if (pane.Mode != ViewMode.Home) pane.PreferredViewMode = pane.Mode;
            }
        }

        public IEnumerable<ExplorerViewModel> EnumerateOwnedExplorers()
            => Tabs.SelectMany(EnumerateTabExplorers).Distinct();

        private static IEnumerable<ExplorerViewModel> EnumerateTabExplorers(TabItem tab)
        {
            if (tab.Explorer != null) yield return tab.Explorer;
            if (tab.SplitRightExplorer != null) yield return tab.SplitRightExplorer;
            if (tab.WorkspaceLayout != null)
                foreach (var pane in tab.WorkspaceLayout.Panes)
                    yield return pane.Explorer;
        }

        private void CleanupTabExplorers(TabItem tab)
        {
            var retained = Tabs.Where(other => !ReferenceEquals(other, tab)).SelectMany(EnumerateTabExplorers).ToHashSet();
            foreach (var explorer in EnumerateTabExplorers(tab).Distinct())
                if (!retained.Contains(explorer)) explorer.Cleanup();
        }
    }
}
