using System;
using System.Windows;
using System.Windows.Controls;

namespace Apex.UI.Models
{
    /// <summary>
    /// Represents a guide line that can be dragged from rulers.
    /// Guides are visual aids that don't appear in print output.
    /// </summary>
    public class GuideLine
    {
        public double Position { get; set; }
        public Orientation Orientation { get; set; }
        public bool IsPrintVisible { get; set; } = false; // Guides never print
        public string Id { get; set; } = Guid.NewGuid().ToString();
    }
}
