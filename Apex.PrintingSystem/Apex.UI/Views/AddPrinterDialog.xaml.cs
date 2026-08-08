using System.Collections.Generic;
using System.Windows;
using Apex.Core.Models;

namespace Apex.UI.Views
{
    public partial class AddPrinterDialog : Window
    {
        public Printer? NewPrinter { get; private set; }
        public PrinterInfo? SelectedPrinterInfo { get; private set; }

        public AddPrinterDialog()
        {
            InitializeComponent();
        }

        public AddPrinterDialog(IEnumerable<PrinterInfo> availablePrinters) : this()
        {
            PrinterListBox.ItemsSource = availablePrinters;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (PrinterListBox.SelectedItem is not PrinterInfo selectedPrinter)
            {
                MessageBox.Show("Please select a printer from the list.", "No Printer Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedPrinterInfo = selectedPrinter;

            NewPrinter = new Printer
            {
                Name = selectedPrinter.Name,
                Type = selectedPrinter.Type,
                Status = selectedPrinter.IsOnline ? "Online" : "Offline",
                IsActive = true
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

