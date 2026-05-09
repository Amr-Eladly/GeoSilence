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
#endif

namespace GeoSilence.Services
{
    public class ModeService
    {
        public void SetMode(ModeType mode)
        {
#if ANDROID

            var context =Android.App.Application.Context;

            var audioManager =
                context.GetSystemService(Context.AudioService)
                as AudioManager;

            var notificationManager =
                context.GetSystemService(Context.NotificationService)
                as NotificationManager;

            if (audioManager == null || notificationManager == null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "AudioManager or NotificationManager is null");

                return;
            }

            // Check DND / Notification Policy permission
            if (!notificationManager.IsNotificationPolicyAccessGranted)
            {
                System.Diagnostics.Debug.WriteLine(
                    "DND permission NOT granted");

                Intent intent =
                    new Intent(Settings.ActionNotificationPolicyAccessSettings);

                intent.AddFlags(ActivityFlags.NewTask);

                context.StartActivity(intent);

                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"RingerMode BEFORE switch: {audioManager.RingerMode}");

            System.Diagnostics.Debug.WriteLine(
                $"Switching to {mode}");

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

            System.Diagnostics.Debug.WriteLine(
                $"RingerMode AFTER switch: {audioManager.RingerMode}");

#endif
        }

#if ANDROID
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
#endif
    }
}
