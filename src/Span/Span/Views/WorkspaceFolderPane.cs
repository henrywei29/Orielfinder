using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using System.ComponentModel;
using Windows.System;

namespace Span.Views
{
    public sealed class WorkspaceFolderPane : UserControl
    {
        private readonly ContextMenuService _contextMenus;
        private readonly IContextMenuHost _contextHost;
        private readonly IntPtr _ownerHwnd;
        private readonly SettingsService _settings;
        private readonly LocalizationService? _localization;
        private readonly Border _frame = new();
        private readonly Grid _header = new();
        private readonly Grid _content = new();
        private readonly ComboBox _modePicker;
        private readonly Button _closeButton;
        private readonly TextBlock _navigationError = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
        private readonly Flyout _errorFlyout;
        private bool _updatingMode;
        private bool _isCleanedUp;
        private bool _isActive;

        public ExplorerViewModel Explorer { get; }
        public ScrollViewer MillerScroller { get; }
        public ItemsControl MillerItems { get; }
        public DetailsModeView? DetailsView { get; private set; }
        public ListModeView? ListView { get; private set; }
        public IconModeView? IconView { get; private set; }
        public HomeModeView? HomeView { get; private set; }
        public ViewMode Mode { get; private set; } = ViewMode.MillerColumns;
        public FrameworkElement MoveGrip { get; }
        public FrameworkElement ResizeGrip { get; }
        public TextBox PathBox { get; }

        public event Action<WorkspaceFolderPane>? Activated;
        public event Action<WorkspaceFolderPane>? CloseRequested;
        public event Action<WorkspaceFolderPane>? ModeChanged;

        public WorkspaceFolderPane(ExplorerViewModel explorer, DataTemplate millerTemplate,
            ItemsPanelTemplate millerItemsPanel, ContextMenuService contextMenus,
            IContextMenuHost contextHost, IntPtr ownerHwnd, SettingsService settings)
        {
            Explorer = explorer;
            _contextMenus = contextMenus;
            _contextHost = contextHost;
            _ownerHwnd = ownerHwnd;
            _settings = settings;
            _localization = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            DataContext = explorer;
            MinWidth = 150;
            _frame.BorderThickness = new Thickness(1);
            _frame.Background = ResourceBrush("SpanBgLayer1Brush");
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Content = _frame;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _frame.Child = root;

            var header = _header;
            header.Height = 32;
            header.ColumnSpacing = 2;
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.BorderBrush = ResourceBrush("SpanBorderSubtleBrush");
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            root.Children.Add(header);

            MoveGrip = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Child = new FontIcon { Glyph = "\uE700", FontSize = 10, IsHitTestVisible = false }
            };
            MoveGrip.Width = 18;
            var navigation = new StackPanel { Orientation = Orientation.Horizontal };
            navigation.Children.Add(MoveGrip);
            var up = new Button { Content = new FontIcon { Glyph = "\uE74A", FontSize = 11 },
                Width = 24, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(2) };
            ToolTipService.SetToolTip(up, "Up");
            up.Click += (_, _) => { Activated?.Invoke(this); Explorer.NavigateUp(); };
            navigation.Children.Add(up);
            var refresh = new Button { Content = new FontIcon { Glyph = "\uE72C", FontSize = 11 },
                Width = 24, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(2) };
            ToolTipService.SetToolTip(refresh, "Refresh (F5)");
            refresh.Click += (_, _) => { Activated?.Invoke(this); if (_contextHost is MainWindow window) window.RefreshActiveFolder(); };
            navigation.Children.Add(refresh);
            header.Children.Add(navigation);

            PathBox = new TextBox
            {
                MinWidth = 0,
                MinHeight = 0,
                Height = 26,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(PathBox, 1);
            header.Children.Add(PathBox);
            PathBox.KeyDown += OnPathKeyDown;
            PathBox.LostFocus += OnPathLostFocus;
            _errorFlyout = new Flyout { Content = _navigationError };

            _modePicker = new ComboBox
            {
                Width = 48,
                Height = 26,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(3, 0, 0, 0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            AddMode(ViewMode.MillerColumns);
            AddMode(ViewMode.Details);
            AddMode(ViewMode.List);
            AddMode(ViewMode.IconMedium);
            _modePicker.SelectedIndex = 0;
            _modePicker.SelectionChanged += OnModeSelectionChanged;
            Grid.SetColumn(_modePicker, 2);
            header.Children.Add(_modePicker);

            _closeButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10 },
                Width = 24,
                Height = 26,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _closeButton.Click += OnCloseClick;
            Grid.SetColumn(_closeButton, 3);
            header.Children.Add(_closeButton);

            MillerItems = new ItemsControl
            {
                ItemTemplate = millerTemplate,
                ItemsPanel = millerItemsPanel,
                ItemsSource = explorer.Columns
            };
            var columns = new StackPanel { Orientation = Orientation.Horizontal };
            columns.Children.Add(MillerItems);
            columns.Children.Add(new Border { Width = 0 });
            MillerScroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                AllowDrop = true,
                Content = columns
            };
            _content.Children.Add(MillerScroller);
            Grid.SetRow(_content, 1);
            root.Children.Add(_content);

            ResizeGrip = new Border
            {
                Width = 8,
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = ResourceBrush("SpanTextTertiaryBrush"),
                BorderThickness = new Thickness(0, 0, 1, 1)
            };
            Grid.SetRow(ResizeGrip, 1);
            root.Children.Add(ResizeGrip);

            AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPanePointerPressed), true);
            GotFocus += OnPaneGotFocus;
            Loaded += OnPaneLoaded;
            ActualThemeChanged += OnActualThemeChanged;
            Explorer.PropertyChanged += OnExplorerPropertyChanged;
            Explorer.NavigationError += OnNavigationError;
            if (_localization != null) _localization.LanguageChanged += Localize;
            Localize();
            UpdatePath();
            SetActive(false);
            SetMode(ViewMode.MillerColumns);
        }

        private Brush? ResourceBrush(string key)
        {
            var root = (_contextHost as MainWindow)?.Content as FrameworkElement;
            var theme = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
                ? "HighContrast" : root?.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            if (root != null && root.Resources.ThemeDictionaries.TryGetValue(theme, out var local)
                && local is ResourceDictionary localTheme
                && localTheme.TryGetValue(key, out var localValue) && localValue is Brush localBrush)
                return localBrush;
            if (App.Current.Resources.ThemeDictionaries.TryGetValue(theme, out var application)
                && application is ResourceDictionary appTheme
                && appTheme.TryGetValue(key, out var appValue) && appValue is Brush appBrush)
                return appBrush;
            return App.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;
        }

        private void AddMode(ViewMode mode) => _modePicker.Items.Add(new ComboBoxItem { Tag = mode });

        public void SetMode(ViewMode mode)
        {
            if (_isCleanedUp) return;
            if (mode != ViewMode.Home && mode != ViewMode.MillerColumns && mode != ViewMode.Details && mode != ViewMode.List
                && !Helpers.ViewModeExtensions.IsIconMode(mode)) return;

            var changed = Mode != mode;
            Mode = mode;
            Explorer.EnableAutoNavigation = mode == ViewMode.MillerColumns && _settings.MillerClickBehavior != "double";
            if (mode == ViewMode.Home && HomeView == null)
            {
                HomeView = new HomeModeView
                {
                    MainViewModel = _contextHost is MainWindow window ? window.ViewModel : null,
                    ContextMenuService = _contextMenus,
                    ContextMenuHost = _contextHost
                };
                _content.Children.Add(HomeView);
            }
            else if (mode == ViewMode.Details && DetailsView == null)
            {
                DetailsView = new DetailsModeView
                {
                    IsManualViewModel = true, ViewModel = Explorer,
                    ContextMenuService = _contextMenus, ContextMenuHost = _contextHost, OwnerHwnd = _ownerHwnd
                };
                DetailsView.ApplyDensity(_settings.Density);
                _content.Children.Add(DetailsView);
            }
            else if (mode == ViewMode.List && ListView == null)
            {
                ListView = new ListModeView
                {
                    IsManualViewModel = true, ViewModel = Explorer,
                    ContextMenuService = _contextMenus, ContextMenuHost = _contextHost, OwnerHwnd = _ownerHwnd
                };
                ListView.ApplyDensity(_settings.Density);
                _content.Children.Add(ListView);
            }
            else if (Helpers.ViewModeExtensions.IsIconMode(mode) && IconView == null)
            {
                IconView = new IconModeView
                {
                    IsManualViewModel = true, ViewModel = Explorer,
                    ContextMenuService = _contextMenus, ContextMenuHost = _contextHost, OwnerHwnd = _ownerHwnd
                };
                IconView.ApplyDensity(_settings.Density);
                IconView.Loaded += OnIconLoaded;
                _content.Children.Add(IconView);
            }

            MillerScroller.Visibility = mode == ViewMode.MillerColumns ? Visibility.Visible : Visibility.Collapsed;
            if (HomeView != null) HomeView.Visibility = mode == ViewMode.Home ? Visibility.Visible : Visibility.Collapsed;
            if (DetailsView != null) DetailsView.Visibility = mode == ViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
            if (ListView != null) ListView.Visibility = mode == ViewMode.List ? Visibility.Visible : Visibility.Collapsed;
            if (IconView != null)
            {
                IconView.Visibility = Helpers.ViewModeExtensions.IsIconMode(mode) ? Visibility.Visible : Visibility.Collapsed;
                if (Helpers.ViewModeExtensions.IsIconMode(mode)) IconView.UpdateIconSize(mode);
            }
            _updatingMode = true;
            _modePicker.SelectedIndex = mode == ViewMode.Home ? -1 : mode == ViewMode.MillerColumns ? 0 : mode == ViewMode.Details ? 1 : mode == ViewMode.List ? 2 : 3;
            _updatingMode = false;
            if (changed)
            {
                ModeChanged?.Invoke(this);
                QueueGeometryDiagnostic();
            }
        }

        private void OnPaneLoaded(object sender, RoutedEventArgs e)
        {
            RefreshTheme();
            QueueGeometryDiagnostic();
        }

        private void QueueGeometryDiagnostic()
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (_isCleanedUp || Mode != ViewMode.MillerColumns) return;
                Helpers.DebugLogger.Log($"[WorkspaceGeometry] pane={ActualWidth:0.##}x{ActualHeight:0.##} " +
                    $"frame={_frame.ActualWidth:0.##}x{_frame.ActualHeight:0.##} " +
                    $"content={_content.ActualWidth:0.##}x{_content.ActualHeight:0.##} " +
                    $"scroll={MillerScroller.ActualWidth:0.##}x{MillerScroller.ActualHeight:0.##} " +
                    $"viewport={MillerScroller.ViewportWidth:0.##}x{MillerScroller.ViewportHeight:0.##} " +
                    $"extent={MillerScroller.ExtentWidth:0.##}x{MillerScroller.ExtentHeight:0.##} " +
                    $"offset={MillerScroller.HorizontalOffset:0.##},{MillerScroller.VerticalOffset:0.##} " +
                    $"scrollable={MillerScroller.ScrollableWidth:0.##},{MillerScroller.ScrollableHeight:0.##}");
            });
        }

        private void OnIconLoaded(object sender, RoutedEventArgs e)
        {
            if (Helpers.ViewModeExtensions.IsIconMode(Mode)) IconView?.UpdateIconSize(Mode);
        }

        public void UpdatePath()
        {
            if (PathBox.FocusState == FocusState.Unfocused) PathBox.Text = Explorer.CurrentPath ?? string.Empty;
            ToolTipService.SetToolTip(PathBox, Explorer.CurrentPath);
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            _frame.BorderBrush = ResourceBrush(active ? "SpanAccentBrush" : "SpanBorderSubtleBrush");
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args) => RefreshTheme();

        private void RefreshTheme()
        {
            Helpers.InlineTreeViewHelper.RefreshStripeTree(_content);
            _frame.Background = ResourceBrush("SpanContentBrush");
            _header.BorderBrush = ResourceBrush("SpanBorderSubtleBrush");
            ((Border)ResizeGrip).BorderBrush = ResourceBrush("SpanTextTertiaryBrush");
            SetActive(_isActive);
        }
        private void OnPanePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.Handled) Activated?.Invoke(this);
        }
        private void OnPaneGotFocus(object sender, RoutedEventArgs e) => Activated?.Invoke(this);
        private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this);
        private void OnPathLostFocus(object sender, RoutedEventArgs e) => UpdatePath();

        private void OnExplorerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExplorerViewModel.CurrentPath)) UpdatePath();
        }

        private async void OnPathKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                PathBox.Text = Explorer.CurrentPath ?? string.Empty;
                e.Handled = true;
                return;
            }
            if (e.Key != VirtualKey.Enter || _isCleanedUp) return;
            e.Handled = true;
            Activated?.Invoke(this);
            var path = PathBox.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path)) return;
            _errorFlyout.Hide();
            try
            {
                if (Mode == ViewMode.Home && _contextHost is MainWindow window)
                    SetMode(window.ViewModel.ResolveViewModeFromHome());
                await Explorer.NavigateToPath(path);
                if (!_isCleanedUp) PathBox.Text = Explorer.CurrentPath ?? string.Empty;
            }
            catch
            {
                OnNavigationError(string.Format(LocalizationService.L("Error_FolderNotFound"), path));
            }
        }

        private void OnNavigationError(string message)
        {
            if (_isCleanedUp) return;
            _navigationError.Text = message;
            if (PathBox.XamlRoot != null) _errorFlyout.ShowAt(PathBox);
        }

        private void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingMode || _modePicker.SelectedItem is not ComboBoxItem { Tag: ViewMode mode }) return;
            Activated?.Invoke(this);
            _settings.LastUsedFolderViewMode = (int)mode;
            SetMode(mode);
        }

        private void Localize()
        {
            var keys = new[] { "Shortcut_ViewMiller", "Shortcut_ViewDetails", "Shortcut_ViewList", "Shortcut_ViewIcon" };
            for (var i = 0; i < keys.Length; i++)
            {
                var item = (ComboBoxItem)_modePicker.Items[i];
                item.Content = LocalizationService.L(keys[i]);
                ToolTipService.SetToolTip(item, item.Content);
            }
            PathBox.PlaceholderText = LocalizationService.L("AddressBar_Placeholder");
            AutomationProperties.SetName(PathBox, PathBox.PlaceholderText);
            AutomationProperties.SetName(MoveGrip, LocalizationService.L("Move"));
            ToolTipService.SetToolTip(MoveGrip, LocalizationService.L("Move"));
            AutomationProperties.SetName(_modePicker, LocalizationService.L("ViewModeSwitch"));
            ToolTipService.SetToolTip(_modePicker, LocalizationService.L("ViewModeSwitch"));
            var chinese = _localization?.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
            var closeText = chinese ? "关闭窗格" : "Close pane";
            var resizeText = chinese ? "调整窗格大小" : "Resize pane";
            AutomationProperties.SetName(_closeButton, closeText);
            ToolTipService.SetToolTip(_closeButton, closeText);
            AutomationProperties.SetName(ResizeGrip, resizeText);
            ToolTipService.SetToolTip(ResizeGrip, resizeText);
        }

        public void Cleanup()
        {
            if (_isCleanedUp) return;
            _isCleanedUp = true;
            Explorer.PropertyChanged -= OnExplorerPropertyChanged;
            Explorer.NavigationError -= OnNavigationError;
            if (_localization != null) _localization.LanguageChanged -= Localize;
            ActualThemeChanged -= OnActualThemeChanged;
            RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPanePointerPressed));
            GotFocus -= OnPaneGotFocus;
            Loaded -= OnPaneLoaded;
            PathBox.KeyDown -= OnPathKeyDown;
            PathBox.LostFocus -= OnPathLostFocus;
            _modePicker.SelectionChanged -= OnModeSelectionChanged;
            _closeButton.Click -= OnCloseClick;
            _errorFlyout.Hide();
            if (IconView != null) IconView.Loaded -= OnIconLoaded;
            DetailsView?.Cleanup();
            ListView?.Cleanup();
            IconView?.Cleanup();
            if (HomeView != null)
            {
                HomeView.MainViewModel = null;
                HomeView.Cleanup();
            }
            MillerItems.ItemsSource = null;
            Activated = null;
            CloseRequested = null;
            ModeChanged = null;
        }
    }
}
