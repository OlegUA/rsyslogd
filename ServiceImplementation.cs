using NLog;
using rsyslogd.Framework;
using System.ServiceProcess;
using System.Threading;


namespace rsyslogd {
    /// <summary>
    /// The actual implementation of the windows service goes here...
    /// </summary>
    [WindowsService("Remote Syslog Service",
        DisplayName = "RSysLog",
        Description = "Simple implementation of classic UNIX remote syslog daemon",
        EventLogSource = "rsyslogd",
        StartMode = ServiceStartMode.Automatic)]
    public class ServiceImplementation : IWindowsService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        ServiceProvider serviceProvider;
        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose() {
        }

        /// <summary>
        /// This method is called when the service gets a request to start.
        /// </summary>
        /// <param name="args">Any command line arguments</param>
        public void OnStart(string[] args) {
            serviceProvider = new ServiceProvider();
            serviceProvider.Start();
        }

        /// <summary>
        /// This method is called when the service gets a request to stop.
        /// </summary>
        public void OnStop() {
            serviceProvider.Stop();
        }

        /// <summary>
        /// This method is called when a service gets a request to pause,
        /// but not stop completely.
        /// </summary>
        public void OnPause() {
        }

        /// <summary>
        /// This method is called when a service gets a request to resume 
        /// after a pause is issued.
        /// </summary>
        public void OnContinue() {
        }

        /// <summary>
        /// This method is called when the machine the service is running on
        /// is being shutdown.
        /// </summary>
        public void OnShutdown() {
            OnStop();
        }

        /// <summary>
        /// This method is called when a custom command is issued to the service.
        /// </summary>
        /// <param name="command">The command identifier to execute.</param >
        public void OnCustomCommand(int command) {
        }
    }
}
