using GeoSilence.Models;

namespace GeoSilence.Services;

internal static class SessionPlaceSchedules
{
    private static ModeService _modeService = new();
    internal static void UseModeService(ModeService modeService) => _modeService = modeService;
    internal static readonly WeeklyScheduleSession Current = new(
        mode => _modeService.ApplyZoneMode(mode),
        () => _modeService.RestoreOriginalMode(),
        zone =>
        {
#if ANDROID
            PlaceActivationNotificationService.ShowActivationRequest(Android.App.Application.Context,
                new PlaceEntity { Id = zone.Id, OwnerId = zone.OwnerId, Name = zone.Name, Mode = (int)zone.Mode });
#endif
        },
        id =>
        {
#if ANDROID
            PlaceActivationNotificationService.CancelActivationRequest(Android.App.Application.Context, id);
#endif
        });

    internal static ScheduleZone Zone(Place place) => new(place.Id, place.OwnerId, place.Name,
        place.Mode, place.ActivationType, place.Distance, place.IsActive && place.Visibility == PlaceVisibility.Private);

    internal static void ObserveGeofence(IEnumerable<PlaceEntity> places, IReadOnlyCollection<string> ids, bool inside)
    {
        foreach (var place in places)
            Current.Observe(new ScheduleZone(place.Id, place.OwnerId, place.Name, (ModeType)place.Mode,
                (ActivationType)place.ActivationType, double.MaxValue, !place.IsDeleted && place.Visibility == (int)PlaceVisibility.Private),
                ids.Contains(place.Id.ToString()) ? inside : null);
        Current.Evaluate();
    }
}
