using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Apex.NumberedBooksEngine.UI.Controls
{
    /// <summary>
    /// Panel for selecting numbering mode (Shershara/Cutting/Auto/Custom).
    /// </summary>
    public partial class NumberingModePanel : UserControl
    {
        public event EventHandler<NumberingMode>? ModeChanged;

        public NumberingModePanel()
        {
            InitializeComponent();
            AutoMode.Checked += OnModeChanged;
            ShersharaMode.Checked += OnModeChanged;
            CuttingMode.Checked += OnModeChanged;
            CustomMode.Checked += OnModeChanged;
        }

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateDescription();
            ModeChanged?.Invoke(this, SelectedMode);
        }

        public NumberingMode SelectedMode
        {
            get
            {
                if (ShersharaMode.IsChecked == true) return NumberingMode.Shershara;
                if (CuttingMode.IsChecked == true) return NumberingMode.Cutting;
                if (CustomMode.IsChecked == true) return NumberingMode.Custom;
                return NumberingMode.Auto;
            }
            set
            {
                AutoMode.IsChecked = value == NumberingMode.Auto;
                ShersharaMode.IsChecked = value == NumberingMode.Shershara || value == NumberingMode.Linear;
                CuttingMode.IsChecked = value == NumberingMode.Cutting || value == NumberingMode.Imposed;
                CustomMode.IsChecked = value == NumberingMode.Custom;
                UpdateDescription();
            }
        }

        private void UpdateDescription()
        {
            ModeDescription.Text = SelectedMode switch
            {
                NumberingMode.Auto => "Auto-detect mode based on slot layout.",
                NumberingMode.Shershara or NumberingMode.Linear => 
                    "Linear top-to-bottom numbering for perforated pads. Numbers: 1, 2, 3, 4...",
                NumberingMode.Cutting or NumberingMode.Imposed => 
                    "Imposed grid numbering for cut sheets. Example: [1, 2501, 5001, 7501]",
                NumberingMode.Custom => "User-defined custom numbering pattern.",
                _ => ""
            };

            DetectedModePanel.Visibility = SelectedMode == NumberingMode.Auto ? 
                Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Updates the detected mode display based on slot analysis.
        /// </summary>
        public void UpdateDetectedMode(IReadOnlyList<SlotSpec>? slots)
        {
            if (slots == null || slots.Count == 0)
            {
                DetectedModeText.Text = "Unable to detect";
                return;
            }

            var detected = LayoutAutoDetector.DetectMode(slots);
            DetectedModeText.Text = detected switch
            {
                NumberingMode.Linear or NumberingMode.Shershara => "Shershara (Linear)",
                NumberingMode.Imposed or NumberingMode.Cutting => "Cutting (Imposed)",
                _ => "Unknown"
            };
        }
    }
}
