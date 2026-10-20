using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Apex.UI.Views.Dialogs
{
    /// <summary>
    /// Asks the operator how many sheets are loaded in each tray of a multi-tray numbering
    /// job, so the engine can hold for a refill before a tray is over-drawn — the firmware
    /// -independent paper-out safeguard. <see cref="TrayLoads"/> maps copy index → sheet count;
    /// it is null when the operator chose "بدون تنبيه" (no counting). DialogResult is false only
    /// when the whole print was cancelled.
    /// </summary>
    public partial class TrayLoadDialog : Window
    {
        private readonly List<(int copyIndex, TextBox box)> _fields = new();

        /// <summary>copy index → declared sheets loaded; null = counting disabled by the operator.</summary>
        public Dictionary<int, int>? TrayLoads { get; private set; }

        public TrayLoadDialog()
        {
            InitializeComponent();
        }

        public TrayLoadDialog(IEnumerable<(int copyIndex, string label)> trays) : this()
        {
            foreach (var t in trays)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

                row.Children.Add(new TextBlock
                {
                    Text = t.label,
                    Width = 160,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x1d)),
                });

                var box = new TextBox
                {
                    Text = "500",
                    Width = 130,
                    FontSize = 14,
                    Padding = new Thickness(8, 6, 8, 6),
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                row.Children.Add(box);

                RowsPanel.Children.Add(row);
                _fields.Add((t.copyIndex, box));
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var dict = new Dictionary<int, int>();
            foreach (var (copyIndex, box) in _fields)
                if (int.TryParse((box.Text ?? "").Trim(), out var n) && n > 0)
                    dict[copyIndex] = n;

            // If nothing valid was entered, treat it as "no counting" rather than blocking.
            TrayLoads = dict.Count > 0 ? dict : null;
            DialogResult = true;
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            TrayLoads = null;   // operator opted out of the refill prompts for this run
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;   // cancel the whole print
            Close();
        }
    }
}
