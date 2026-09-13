namespace Apex.UI
{
    /// <summary>
    /// Status colours for text a ViewModel sets at runtime — "saved", "not found", "scanning".
    ///
    /// <para>These used to be the stock web palette written inline in each ViewModel:
    /// green #22C55E, amber #F59E0B, red #EF4444, blue #3B82F6. As text on the app's light
    /// ground they measured 2.28, 2.15 and 3.76:1 — the amber and green far below the 4.5:1
    /// small text needs, on exactly the messages that tell an operator whether an export or an
    /// activation worked. They also matched nothing in Brand.xaml.</para>
    ///
    /// <para>Values mirror the Brand.xaml tokens (Done, Stopped, Printing, Action). Keep them in
    /// step: XAML reads the tokens, code reads these.</para>
    /// </summary>
    public static class StatusPalette
    {
        /// <summary>Succeeded. 5.74:1 on white.</summary>
        public const string Done = "#166B40";

        /// <summary>Failed, needs the operator. 6.57:1 on white.</summary>
        public const string Error = "#B42318";

        /// <summary>Worth a look but not blocking. 5.02:1 on white.</summary>
        public const string Warning = "#B45309";

        /// <summary>Neutral progress — "scanning", a hint. 5.36:1 on white.</summary>
        public const string Info = "#0E7490";

        /// <summary>Not reached yet (step indicators). A boundary colour, 3.52:1 on white.</summary>
        public const string Pending = "#8F887A";

        /// <summary>The identity cyan for outlines and selection. 3.68:1 on white — never text.</summary>
        public const string Brand = "#0891B2";

        /// <summary>A ~10% Brand tint for a selected area's fill.</summary>
        public const string BrandTint = "#190891B2";
    }
}
