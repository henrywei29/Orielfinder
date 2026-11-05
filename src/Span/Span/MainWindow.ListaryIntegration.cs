using Span.Helpers;
using Span.Models;
using Span.Services;

namespace Span
{
    public sealed partial class MainWindow
    {
        private ListaryWindowBridge? _listaryBridge;

        private string GetListaryFolderPath()
        {
            if (_isClosed) return string.Empty;
            var mode = ViewModel.IsSplitViewEnabled && ViewModel.ActivePane == ActivePane.Right
                ? ViewModel.RightViewMode : ViewModel.CurrentViewMode;
            if (mode != ViewMode.MillerColumns && mode != ViewMode.Details && mode != ViewMode.List
                && !ViewModeExtensions.IsIconMode(mode)) return string.Empty;
            return ListaryFolderPath.Normalize(GetNewItemTargetFolder()?.Path ?? string.Empty);
        }
    }
}
