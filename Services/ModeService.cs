using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoSilence.Models;

#if ANDROID
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Provider;
using GeoSilence.Platforms.Droid;
#endif

namespace GeoSilence.Services
{
    public class ModeService
    {
        public void ApplyZoneMode(ModeType mode)
        {
#if ANDROID
            CaptureOriginalMode();
#endif
            SetMode(mode);
        }

        public void RestoreOriginalMode()
        {
#if ANDROID
            _originalMode ??= ReadOriginalMode();

            if (_originalMode == null)
                return;

            var managers = GetManagers();

            if (managers == null)
                return;

            var (audioManager, notificationManager) = managers.Value;

            if (_originalMode.InterruptionFilter.HasValue &&
                _originalMode.InterruptionFilter.Value != InterruptionFilter.None)
            {
                SetInterruptionFilter(
                    notificationManager,
                    _originalMode.InterruptionFilter.Value);
            }

            audioManager.RingerMode = _originalMode.RingerMode;

            if (_originalMode.InterruptionFilter.HasValue)
            {
                SetInterruptionFilter(
                    notificationManager,
                    _originalMode.InterruptionFilter.Value);
            }

            GeoLog.Write("MODE", $"restored original ringer={_originalMode.RingerMode}");

            _originalMode = null;
            ClearOriginalMode();
#endif
        }

        public void SetMode(ModeType mode)
        {
#if ANDROID

            var managers = GetManagers();

            if (managers == null)
                return;

            var (audioManager, notificationManager) = managers.Value;

            // Check DND / Notification Policy permission. From a background
            // BroadcastReceiver context we MUST NOT call StartActivity for
            // settings — Android 14+ blocks non-Activity Activity launches and
            // it's hostile UX besides. Just log and bail; the user grants this
            // once on first foreground launch (handled by MainActivity).
            if (!notificationManager.IsNotificationPolicyAccessGranted)
            {
                GeoLog.Write("MODE", "ABORT: DND/Notification Policy access not granted");
                return;
            }

            GeoLog.Write("MODE", $"applying mode={mode} ringerBefore={audioManager.RingerMode}");

            switch (mode)
            {
                case ModeType.Silent:

                    // On Android M+ true silence is controlled through DND.
                    // Muting only the ring stream can leave the volume UI showing
                    // silent while notifications/calls are still allowed through.
                    audioManager.RingerMode = RingerMode.Silent;
                    SetInterruptionFilter(
                        notificationManager,
                        InterruptionFilter.None);

                    break;

                case ModeType.Vibrate:

                    SetInterruptionFilter(
                        notificationManager,
                        InterruptionFilter.All);

                    audioManager.RingerMode = RingerMode.Vibrate;

                    break;

                case ModeType.Normal:

                    SetInterruptionFilter(
                        notificationManager,
                        InterruptionFilter.All);

                    audioManager.RingerMode = RingerMode.Normal;

                    // Unmute ringtone stream
                    audioManager.AdjustStreamVolume(
                        Android.Media.Stream.Ring,
                        Adjust.Unmute,
                        VolumeNotificationFlags.PlaySound);

                    // Unmute notification stream
                    audioManager.AdjustStreamVolume(
                        Android.Media.Stream.Notification,
                        Adjust.Unmute,
                        VolumeNotificationFlags.PlaySound);

                    break;
            }

            GeoLog.Write("MODE", $"done mode={mode} ringerAfter={audioManager.RingerMode}");

#endif
        }

#if ANDROID
        private AndroidModeState? _originalMode;

        private const string ModePrefsName = "GeoSilenceModeState";
        private const string HasOriginalModeKey = "HasOriginalMode";
        private const string OriginalRingerModeKey = "OriginalRingerMode";
        private const string OriginalInterruptionFilterKey = "OriginalInterruptionFilter";

        private static (AudioManager AudioManager, NotificationManager NotificationManager)? GetManagers()
        {
            var context = Android.App.Application.Context;

            var audioManager =
                context.GetSystemService(Context.AudioService)
                as AudioManager;

            var notificationManager =
                context.GetSystemService(Context.NotificationService)
                as NotificationManager;

            if (audioManager == null || notificationManager == null)
            {
                GeoLog.Write("MODE", "ABORT: AudioManager or NotificationManager null");
                return null;
            }

            return (audioManager, notificationManager);
        }

        private void CaptureOriginalMode()
        {
            if (_originalMode != null)
                return;

            _originalMode = ReadOriginalMode();

            if (_originalMode != null)
                return;

            var managers = GetManagers();

            if (managers == null)
                return;

            var (audioManager, notificationManager) = managers.Value;

            _originalMode = new AndroidModeState
            {
                RingerMode = audioManager.RingerMode,
                InterruptionFilter = Build.VERSION.SdkInt >= BuildVersionCodes.M
                    ? notificationManager.CurrentInterruptionFilter
                    : null
            };

            SaveOriginalMode(_originalMode);

            GeoLog.Write("MODE", $"captured original ringer={_originalMode.RingerMode}");
        }

        private static void SaveOriginalMode(AndroidModeState mode)
        {
            var prefs = Android.App.Application.Context.GetSharedPreferences(
                ModePrefsName,
                FileCreationMode.Private);

            prefs!
                .Edit()!
                .PutBoolean(HasOriginalModeKey, true)
                .PutInt(OriginalRingerModeKey, (int)mode.RingerMode)
                .PutInt(
                    OriginalInterruptionFilterKey,
                    mode.InterruptionFilter.HasValue
                        ? (int)mode.InterruptionFilter.Value
                        : -1)
                .Apply();
        }

        private static AndroidModeState? ReadOriginalMode()
        {
            var prefs = Android.App.Application.Context.GetSharedPreferences(
                ModePrefsName,
                FileCreationMode.Private);

            if (prefs == null ||
                !prefs.GetBoolean(HasOriginalModeKey, false))
                return null;

            var filterValue = prefs.GetInt(OriginalInterruptionFilterKey, -1);

            return new AndroidModeState
            {
                RingerMode = (RingerMode)prefs.GetInt(
                    OriginalRingerModeKey,
                    (int)RingerMode.Normal),
                InterruptionFilter = filterValue >= 0
                    ? (InterruptionFilter)filterValue
                    : null
            };
        }

        private static void ClearOriginalMode()
        {
            var prefs = Android.App.Application.Context.GetSharedPreferences(
                ModePrefsName,
                FileCreationMode.Private);

            prefs!
                .Edit()!
                .Remove(HasOriginalModeKey)
                .Remove(OriginalRingerModeKey)
                .Remove(OriginalInterruptionFilterKey)
                .Apply();
        }

        private static void SetInterruptionFilter(
            NotificationManager notificationManager,
            InterruptionFilter filter)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.M)
                return;

            if (!notificationManager.IsNotificationPolicyAccessGranted)
                return;

            notificationManager.SetInterruptionFilter(filter);
        }

        private sealed class AndroidModeState
        {
            public RingerMode RingerMode { get; init; }
            public InterruptionFilter? InterruptionFilter { get; init; }
        }
#endif
    }
}
