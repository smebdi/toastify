﻿using GoogleMeasurementProtocol;
using GoogleMeasurementProtocol.Parameters;
using GoogleMeasurementProtocol.Parameters.AppTracking;
using GoogleMeasurementProtocol.Parameters.ContentInformation;
using GoogleMeasurementProtocol.Parameters.EventTracking;
using GoogleMeasurementProtocol.Parameters.Exceptions;
using GoogleMeasurementProtocol.Parameters.Hit;
using GoogleMeasurementProtocol.Parameters.SystemInfo;
using GoogleMeasurementProtocol.Parameters.User;
using GoogleMeasurementProtocol.Requests;
using log4net;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using Toastify.Core;
using Toastify.Model;

namespace Toastify.Services
{
    // ReSharper disable once PartialTypeWithSinglePart
    public static partial class Analytics
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(Analytics));

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        private static string TrackingId { get; set; }

        public static bool AnalyticsEnabled
        {
#if !TEST_RELEASE
            get { return requestFactory != null && Settings.Current.OptInToAnalytics; }
#else
            get { return false; }
#endif
        }

        private static GoogleAnalyticsRequestFactory requestFactory;

        internal static void Init()
        {
#if !TEST_RELEASE
            if (logger.IsDebugEnabled)
                logger.Debug("Initializing Analytics class...");

            // ReSharper disable once InvocationIsSkipped
            SetTrackingId();

            if (string.IsNullOrWhiteSpace(TrackingId))
            {
                logger.Error("No TrackingId set.");
                return;
            }

            requestFactory = new GoogleAnalyticsRequestFactory(TrackingId);
            bool wasOnNoAnalyticsVersion = new Version(Settings.Current.PreviousVersion ?? "0.0.0") < new Version("1.9.7");
            bool appHasBeenJustUpdated = new Version(Settings.Current.PreviousVersion ?? "0.0.0") < new Version(VersionChecker.CurrentVersion);

            // Install Event
            if (Settings.Current.FirstRun || wasOnNoAnalyticsVersion)
            {
                TrackInstallEvent();
                Settings.Current.FirstRun = false;
            }

            // NOTE: The install event should always be sent
            if (!AnalyticsEnabled)
                return;

            // Collect Preferences every at every update
            if (appHasBeenJustUpdated)
                CollectPreferences();
#else
            requestFactory = null;
#endif
        }

        #region TrackPageHit

        public static void TrackPageHit(string documentPath)
        {
            TrackPageHit(documentPath, null, true);
        }

        public static void TrackPageHit(string documentPath, string title)
        {
            TrackPageHit(documentPath, title, true);
        }

        public static void TrackPageHit(string documentPath, bool interactive)
        {
            TrackPageHit(documentPath, null, interactive);
        }

        public static void TrackPageHit(string documentPath, string title, bool interactive)
        {
            if (!AnalyticsEnabled)
                return;

            var request = requestFactory.CreateRequest(HitTypes.PageView);

            request.Parameters.AddRange(GetCommonParameters());
            request.Parameters.Add(new DocumentHostName("github.com/aleab/toastify"));
            request.Parameters.Add(new DocumentPath(documentPath));
            if (title != null)
                request.Parameters.Add(new DocumentTitle(title));
            request.Parameters.Add(new NonInteractionHit(!interactive));

            PostRequest(request);

            if (logger.IsDebugEnabled)
                logger.Debug($"[Analytics] PageHit: ni={!interactive}, dp=\"{documentPath}\", dt=\"{title}\"");
        }

        #endregion TrackPageHit

        #region TrackEvent

        public static void TrackEvent(ToastifyEventCategory eventCategory, string eventAction)
        {
            TrackEvent(eventCategory, eventAction, null, -1);
        }

        public static void TrackEvent(ToastifyEventCategory eventCategory, string eventAction, string eventLabel)
        {
            TrackEvent(eventCategory, eventAction, eventLabel, -1);
        }

        public static void TrackEvent(ToastifyEventCategory eventCategory, string eventAction, int eventValue)
        {
            TrackEvent(eventCategory, eventAction, null, eventValue);
        }

        public static void TrackEvent(ToastifyEventCategory eventCategory, string eventAction, string eventLabel, int eventValue)
        {
            TrackEvent(eventCategory, eventAction, eventLabel, eventValue, null);
        }

        private static void TrackEvent(ToastifyEventCategory eventCategory, string eventAction, string eventLabel, int eventValue, IEnumerable<Parameter> extraParameters)
        {
            if (!AnalyticsEnabled)
                return;

            var request = requestFactory.CreateRequest(HitTypes.Event);

            request.Parameters.AddRange(GetCommonParameters());
            request.Parameters.Add(new EventCategory(eventCategory.ToString()));
            request.Parameters.Add(new EventAction(eventAction));
            if (eventLabel != null)
                request.Parameters.Add(new EventLabel(eventLabel));
            if (eventValue >= 0)
                request.Parameters.Add(new EventValue(eventValue));
            if (extraParameters != null)
                request.Parameters.AddRange(extraParameters);

            PostRequest(request);

            if (logger.IsDebugEnabled)
                logger.Debug($"[Analytics] Event: ec=\"{eventCategory}\", ea=\"{eventAction}\", el=\"{eventLabel}\", ev=\"{eventValue}\"");
        }

        #endregion TrackEvent

        #region TrackException

        public static void TrackException(Exception exception)
        {
            TrackException(exception, false);
        }

        public static void TrackException(Exception exception, bool fatal)
        {
            if (!AnalyticsEnabled)
                return;

            // The exception will be truncated to 150 bytes; at some point it may be better to extract more pertinent information.
            var request = requestFactory.CreateRequest(HitTypes.Exception, GetCommonParameters());

            request.Parameters.AddRange(GetCommonParameters());
            request.Parameters.Add(new ExceptionDescription($"{exception}"));
            request.Parameters.Add(new IsExceptionFatal(fatal));

            PostRequest(request);
        }

        #endregion TrackException

        private static void PostRequest(IGoogleAnalyticsRequest request)
        {
            request?.Post(new ClientId(GetMachineID()));
        }

        private static IEnumerable<Parameter> GetCommonParameters()
        {
            var parameters = new List<Parameter>
            {
                new ApplicationName("Toastify"),
                new ApplicationVersion(VersionChecker.CurrentVersion)
            };
            return parameters;
        }

        private static void TrackInstallEvent()
        {
            IEnumerable<Parameter> extraParameters = new List<Parameter>
            {
                new UserLanguage(CultureInfo.CurrentUICulture.Name)
            };
            TrackEvent(ToastifyEventCategory.General, "Install", GetOS(), -1, extraParameters);
        }

        private static void TrackSettingBinaryHit(string settingName, bool track)
        {
            if (track)
                TrackPageHit($"/{VersionChecker.CurrentVersion}/Settings/{settingName}", null, false);
        }

        private static void CollectPreferences()
        {
            if (logger.IsDebugEnabled)
                logger.Debug($"Collecting preferences...");

            // General
            TrackSettingBinaryHit(nameof(Settings.Current.LaunchOnStartup), Settings.Current.LaunchOnStartup);
            TrackSettingBinaryHit(nameof(Settings.Current.MinimizeSpotifyOnStartup), Settings.Current.MinimizeSpotifyOnStartup);
            TrackSettingBinaryHit(nameof(Settings.Current.CloseSpotifyWithToastify), Settings.Current.CloseSpotifyWithToastify);

            TrackSettingBinaryHit($"{nameof(Settings.Current.VolumeControlMode)}/{ToastifyVolumeControlMode.Spotify}", Settings.Current.VolumeControlMode == ToastifyVolumeControlMode.Spotify);
            TrackSettingBinaryHit($"{nameof(Settings.Current.VolumeControlMode)}/{ToastifyVolumeControlMode.SystemGlobal}", Settings.Current.VolumeControlMode == ToastifyVolumeControlMode.SystemGlobal);
            TrackSettingBinaryHit($"{nameof(Settings.Current.VolumeControlMode)}/{ToastifyVolumeControlMode.SystemSpotifyOnly}", Settings.Current.VolumeControlMode == ToastifyVolumeControlMode.SystemSpotifyOnly);

            TrackSettingBinaryHit(nameof(Settings.Current.SaveTrackToFile), Settings.Current.SaveTrackToFile);

            // Hotkeys
            TrackSettingBinaryHit(nameof(Settings.Current.GlobalHotKeys), Settings.Current.GlobalHotKeys);
            foreach (var hotkey in Settings.Current.HotKeys)
                TrackSettingBinaryHit($"HotKeys/{hotkey.Action}", hotkey.Enabled);

            // Toast
            TrackSettingBinaryHit(nameof(Settings.Current.DisableToast), Settings.Current.DisableToast);
            TrackSettingBinaryHit(nameof(Settings.Current.OnlyShowToastOnHotkey), Settings.Current.OnlyShowToastOnHotkey);
            TrackSettingBinaryHit(nameof(Settings.Current.DisableToastWithFullscreenVideogames), Settings.Current.DisableToastWithFullscreenVideogames);
            TrackSettingBinaryHit(nameof(Settings.Current.ShowSongProgressBar), Settings.Current.ShowSongProgressBar);

            TrackSettingBinaryHit($"{nameof(Settings.Current.ToastTitlesOrder)}/{ToastTitlesOrder.ArtistOfTrack}", Settings.Current.ToastTitlesOrder == ToastTitlesOrder.ArtistOfTrack);
            TrackSettingBinaryHit($"{nameof(Settings.Current.ToastTitlesOrder)}/{ToastTitlesOrder.TrackByArtist}", Settings.Current.ToastTitlesOrder == ToastTitlesOrder.TrackByArtist);
        }

        private static string GetOS()
        {
            return Environment.OSVersion.VersionString +
                " (" + GetFriendlyOS() + ")" +
                " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")";
        }

        private static string GetFriendlyOS()
        {
            object name = null;
            try
            {
                var managementObjects = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem")
                    .Get()
                    .OfType<ManagementObject>();
                name = (from x in managementObjects select x.GetPropertyValue("Caption")).FirstOrDefault();
            }
            catch (Exception e)
            {
                logger.Error("Error while getting FriendlyOS.", e);
            }
            return name?.ToString() ?? "Unknown";
        }

        private static string GetMachineID()
        {
            // HKLM\SOFTWARE\Microsoft\Cryptography > MachineGuid
            string machineGuid = Registry.LocalMachine
                ?.OpenSubKey("SOFTWARE")
                ?.OpenSubKey("Microsoft")
                ?.OpenSubKey("Cryptography")
                ?.GetValue("MachineGuid") as string;

            return machineGuid ?? "00000000-0000-0000-0000-000000000000";
        }

        // ReSharper disable once PartialMethodWithSinglePart
        static partial void SetTrackingId();

        public enum ToastifyEventCategory
        {
            General,
            Action
        }

        /// <summary>
        /// Poor mans enum -> expanded string.
        ///
        /// Once I've been using this for a while I may change this to a pure enum if
        /// spaces in names prove to be annoying for querying / sorting the data
        /// </summary>
        public static class ToastifyEvent
        {
            public static string Exception { get; } = "Exception";

            public static string AppLaunch { get; } = "Toastify.AppLaunched";
            public static string AppTermination { get; } = "Toastify.AppTermination";
            public static string SettingsLaunched { get; } = "Toastify.SettingsLaunched";

            public static class Action
            {
                public static string Mute { get; } = "Toastify.Action.Mute";
                public static string VolumeDown { get; } = "Toastify.Action.VolumeDown";
                public static string VolumeUp { get; } = "Toasitfy.Action.VolumeUp";
                public static string ShowToast { get; } = "Toastify.Action.ShowToast";
                public static string ShowSpotify { get; } = "Toastify.Action.ShowSpotify";
                public static string CopyTrackInfo { get; } = "Toastify.Action.CopyTrackInfo";
                public static string PasteTrackInfo { get; } = "Toastify.Action.PasteTrackInfo";
                public static string FastForward { get; } = "Toastify.Action.FastForward";
                public static string Rewind { get; } = "Toastify.Action.Rewind";
                public static string Default { get; } = "Toastify.Action.";
            }
        }
    }
}