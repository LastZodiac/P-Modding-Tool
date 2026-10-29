namespace SkinManager.Core
{
    /// <summary>BrawlLib can't open two files at the same time safely: every use goes through this lock.
    /// Its own pop-up windows (progress bars, "model has errors") are switched off before the first use.</summary>
    public static class BrawlGate
    {
        public static readonly object Lock = new object();
        static BrawlGate() { Installer.Quiet(); }
    }
}
