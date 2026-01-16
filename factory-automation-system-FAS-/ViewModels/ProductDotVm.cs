// ViewModels/ProductDotVm.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace factory_automation_system_FAS_.ViewModels
{
    public sealed class ProductDotVm : INotifyPropertyChanged
    {
        private double _x;
        public double X { get => _x; set { _x = value; OnPropertyChanged(); } }

        private double _y;
        public double Y { get => _y; set { _y = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}