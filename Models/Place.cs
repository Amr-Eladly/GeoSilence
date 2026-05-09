using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GeoSilence.Models
{
    public partial class Place : ObservableObject
    {
        public int Id { get; set; }

        [ObservableProperty]
        private string name;

        [ObservableProperty]
        private double distance;

        public string DistanceFormatted =>
            Distance < 1000 ? $"{(int)Distance} m" : $"{Distance / 1000:F1} km";

        [ObservableProperty]
        private double latitude;

        [ObservableProperty]
        private double longitude;

        [ObservableProperty]
        private double radius;

        [ObservableProperty]
        private ModeType mode;

        [ObservableProperty]
        private bool isActive = true;

        // IMPORTANT
        partial void OnDistanceChanged(double value)
        {
            OnPropertyChanged(nameof(DistanceFormatted));
        }
    }
}