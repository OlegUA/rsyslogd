using RSCVCommon;

namespace rsyslogd {
    /// <summary>
    /// Implements needed settings for PLC Helper class
    /// </summary>
    public sealed class SyslogSettings : RSCVSettings {
        #region singleton pattern
        // http://www.yoda.arachsys.com/csharp/singleton.html
        private static readonly SyslogSettings instance = new SyslogSettings();
        /// <summary>
        /// Singleton definition
        /// </summary>
        public static SyslogSettings INSTANCE { get { return instance; } }

        /// <summary>
        /// Explicit static constructor to tell C# compiler not to mark type as before field init
        /// </summary>
        static SyslogSettings() { }

        private SyslogSettings() {
            Load();
        }
        #endregion
        public int Port { get; set; } = 514;
        public string LogDirectory { get; set; } = @"Log";
        public int RotateSizeMB { get; set; } = 10;
        public int RotateCount { get; set; } = 5;
        public bool ShowRemoteDateAndTime { get; set; } = false;

        protected override string ConfigName { get; set; } = @"\SyslogSettings.json";
    }
}
