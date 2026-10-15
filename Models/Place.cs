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

        public string CloudId { get; set; } = string.Empty;

        public string OwnerId { get; set; } = string.Empty;

        [ObservableProperty]
        private string name = string.Empty;

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
        private ActivationType activationType = ActivationType.Automatic;

        [ObservableProperty]
        private PlaceType placeType = PlaceType.Other;

        [ObservableProperty]
        private PlaceVisibility visibility = PlaceVisibility.Private;

        [ObservableProperty]
        private bool isActive = true;

        [ObservableProperty]
        private bool isSelected;

        [ObservableProperty]
        private bool isCloudSyncPending;

        [ObservableProperty]
        private bool hasCloudSyncError;

        public string TypeIcon => PlaceType.ToIcon();
        public string PlaceMetadata =>
            (HasCloudSyncError ? "Backup waiting · " :
                IsCloudSyncPending ? "Backup pending · " : string.Empty) +
            $"{DistanceFormatted} away · Radius: {Radius:F0} m";
        public string ModeLabel => Mode switch
        {
            ModeType.Silent => "● Silent",
            ModeType.Vibrate => "● Vibrate",
            _ => "● Ring"
        };

        partial void OnModeChanged(ModeType value) => OnPropertyChanged(nameof(ModeLabel));
        partial void OnPlaceTypeChanged(PlaceType value) => OnPropertyChanged(nameof(TypeIcon));
        partial void OnRadiusChanged(double value) => OnPropertyChanged(nameof(PlaceMetadata));
        partial void OnIsCloudSyncPendingChanged(bool value) => OnPropertyChanged(nameof(PlaceMetadata));
        partial void OnHasCloudSyncErrorChanged(bool value) => OnPropertyChanged(nameof(PlaceMetadata));

        // IMPORTANT
        partial void OnDistanceChanged(double value)
        {
            OnPropertyChanged(nameof(DistanceFormatted));
            OnPropertyChanged(nameof(PlaceMetadata));
        }
    }
}
