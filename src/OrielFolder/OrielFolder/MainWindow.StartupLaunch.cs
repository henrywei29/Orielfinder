using System;

namespace OrielFolder
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Settings → "Launch at startup": when Windows starts the app at sign-in, the first main
        /// window goes to the background instead of popping up — to the tray if "Close to tray" is on
        /// (same as closing the last window), otherwise minimized to the taskbar.
        /// </summary>
        private void SendToBackgroundIfStartupLaunch()
        {
            if (_isClosed || _isTearOffWindow || !App.ConsumeStartupTaskLaunch()) return;
            try
            {
                if (_settings.MinimizeToTray)
                {
                    AppWindow.Hide();
                    (App.Current.Services.GetService(typeof(Services.TrayIconService)) as Services.TrayIconService)
                        ?.SyncWithSetting();
                    Helpers.DebugLogger.Log("[StartupLaunch] Started by startup task → hidden to tray");
                }
                else if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                    Helpers.DebugLogger.Log("[StartupLaunch] Started by startup task → minimized");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[StartupLaunch] Send to background failed: {ex.Message}");
            }
        }
    }
}
