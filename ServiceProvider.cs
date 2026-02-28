using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace rsyslogd {
    public class ServiceProvider {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        Task listenTask;

        public ServiceProvider() {
        }

        public void Start() {
            listenTask = Syslog.HandleIncomingMessages(cancellationTokenSource.Token);
            logger.Info("Started");
        }
        public void Stop() {
            logger.Info($"Cancellation request...");
            cancellationTokenSource.Cancel();
            listenTask.Wait(200);
            logger.Info("Stopped");
        }
    }
}
