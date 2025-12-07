using Apex.NumberedBooksEngine.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Apex.NumberedBooksEngine.UI
{
    public class SlotViewModel : INotifyPropertyChanged
    {
        private SlotSpec _model;

        public SlotViewModel(SlotSpec model)
        {
            _model = model;
        }

        public SlotSpec Model => _model;

        public string Id => _model.Id;

        public float X
        {
            get => _model.X;
            set { if (_model.X != value) { _model = _model with { X = value }; OnPropertyChanged(); } }
        }

        public float Y
        {
            get => _model.Y;
            set { if (_model.Y != value) { _model = _model with { Y = value }; OnPropertyChanged(); } }
        }

        public float Width
        {
            get => _model.Width;
            set { if (_model.Width != value) { _model = _model with { Width = value }; OnPropertyChanged(); } }
        }

        public float Height
        {
            get => _model.Height;
            set { if (_model.Height != value) { _model = _model with { Height = value }; OnPropertyChanged(); } }
        }

        public string FontFamily
        {
            get => _model.FontFamily;
            set { if (_model.FontFamily != value) { _model = _model with { FontFamily = value }; OnPropertyChanged(); } }
        }

        public float FontSize
        {
            get => _model.FontSize;
            set { if (_model.FontSize != value) { _model = _model with { FontSize = value }; OnPropertyChanged(); } }
        }

        public string FontColorHex
        {
            get => _model.FontColorHex;
            set { if (_model.FontColorHex != value) { _model = _model with { FontColorHex = value }; OnPropertyChanged(); } }
        }

        public TextAlign Align
        {
            get => _model.Align;
            set { if (_model.Align != value) { _model = _model with { Align = value }; OnPropertyChanged(); } }
        }

        public float Rotation
        {
            get => _model.Rotation;
            set { if (_model.Rotation != value) { _model = _model with { Rotation = value }; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
