namespace OrielFolder.Helpers
{
    /// <summary>
    /// Decides when the cloaked startup window is revealed: after <see cref="FramesBeforeReveal"/>
    /// rendering callbacks (so a frame with the restored content has been presented), or by the
    /// idle fallback — whichever comes first, exactly once. Pure logic, single UI thread.
    /// </summary>
    public sealed class StartupRevealGate
    {
        public const int FramesBeforeReveal = 2;

        private int _frames;

        public bool Revealed { get; private set; }

        /// <summary>Called from CompositionTarget.Rendering; true when this call should reveal.</summary>
        public bool OnFrame()
        {
            if (Revealed) return false;
            if (++_frames < FramesBeforeReveal) return false;
            Revealed = true;
            return true;
        }

        /// <summary>Idle/closing fallback; true when the window has not been revealed yet.</summary>
        public bool TryRevealNow()
        {
            if (Revealed) return false;
            Revealed = true;
            return true;
        }
    }
}
