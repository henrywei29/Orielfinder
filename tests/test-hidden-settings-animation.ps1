$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $repo 'src/Span/Span/Views/SettingsModeView.xaml.cs') -Raw
$signature = 'private void UpdateHeartAnimation()'
$start = $source.IndexOf($signature)
if ($start -ge 0) {
    $open = $source.IndexOf('{', $start)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        elseif ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $body = $source.Substring($open + 1, $end - $open - 2)
} else {
    $loaded = [regex]::Match($source, 'this.Loaded \+= \(s, e\) => \{ (?<body>.*?) \};')
    if (-not $loaded.Success) { throw 'Cannot locate settings animation startup.' }
    $body = $loaded.Groups['body'].Value
}
$fixture = @'
using System;
public enum Visibility { Visible, Collapsed }
public class Panel { public Visibility Visibility; }
public class Storyboard {
    public int Starts;
    public bool Running;
    public void Begin() { Starts++; Running = true; }
    public void Stop() { Running = false; }
}
public class AnimationFixture {
    public bool IsLoaded = true;
    public Visibility Visibility;
    public Panel LumiBearPanel = new();
    public Storyboard HeartAnimation = new();
    public void UpdateHeartAnimation() { BODY }
    public static void Verify() {
        var view = new AnimationFixture { Visibility = Visibility.Collapsed };
        view.LumiBearPanel.Visibility = Visibility.Collapsed;
        view.UpdateHeartAnimation();
        if (view.HeartAnimation.Starts != 0) throw new Exception("Hidden settings started a permanent animation.");
        view.Visibility = Visibility.Visible;
        view.UpdateHeartAnimation();
        if (view.HeartAnimation.Starts != 0) throw new Exception("Hidden decoration started a permanent animation.");
        view.LumiBearPanel.Visibility = Visibility.Visible;
        view.UpdateHeartAnimation();
        if (!view.HeartAnimation.Running) throw new Exception("Visible decoration lost its animation.");
        view.Visibility = Visibility.Collapsed;
        view.UpdateHeartAnimation();
        if (view.HeartAnimation.Running) throw new Exception("Settings hidden but animation still running.");
        view.Visibility = Visibility.Visible;
        view.IsLoaded = false;
        view.UpdateHeartAnimation();
        if (view.HeartAnimation.Running) throw new Exception("Unloaded settings kept animating.");
    }
}
'@
Add-Type ($fixture.Replace('BODY', $body))
[AnimationFixture]::Verify()
Write-Output 'PASS: hidden/unloaded settings and decoration do not animate; visible decoration remains animated.'
