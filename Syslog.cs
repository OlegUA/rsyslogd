using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace rsyslogd {

    public static class Syslog {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        /*
         * Calculation: PRI = (Facility * 8) + Severity
         * Facility Codes: 0-23 (e.g., 1 = user-level, 4 = auth, 16 = local0).
         * Severity Codes: 0-7 (e.g., 0 = Emergency, 4 = Warning, 6 = Informational, 7 = Debug).
         */
        //#define LOG_EMERG       0       /* system is unusable */
        //#define LOG_ALERT       1       /* action must be taken immediately */
        //#define LOG_CRIT        2       /* critical conditions */
        //#define LOG_ERR         3       /* error conditions */
        //#define LOG_WARNING     4       /* warning conditions */
        //#define LOG_NOTICE      5       /* normal but significant condition */
        //#define LOG_INFO        6       /* informational */
        //#define LOG_DEBUG       7       /* debug-level messages */
        static string[] _severityNames = {
            "EMERG",
            "ALERT",
            "CRIT",
            "ERR",
            "WARNING",
            "NOTICE",
            "INFO",
            "DEBUG"
        };
        /* facility codes */
        //#define LOG_KERN        (0<<3)  /* kernel messages */
        //#define LOG_USER        (1<<3)  /* random user-level messages */
        //#define LOG_MAIL        (2<<3)  /* mail system */
        //#define LOG_DAEMON      (3<<3)  /* system daemons */
        //#define LOG_AUTH        (4<<3)  /* security/authorization messages */
        //#define LOG_SYSLOG      (5<<3)  /* messages generated internally by syslogd */
        //#define LOG_LPR         (6<<3)  /* line printer subsystem */
        //#define LOG_NEWS        (7<<3)  /* network news subsystem */
        //#define LOG_UUCP        (8<<3)  /* UUCP subsystem */
        //#define LOG_CRON        (9<<3)  /* clock daemon */
        //#define LOG_AUTHPRIV    (10<<3) /* security/authorization messages (private) */
        //#define LOG_FTP         (11<<3) /* ftp daemon */

        /* other codes through 15 reserved for system use */
        //#define LOG_LOCAL0      (16<<3) /* reserved for local use */
        //#define LOG_LOCAL1      (17<<3) /* reserved for local use */
        //#define LOG_LOCAL2      (18<<3) /* reserved for local use */
        //#define LOG_LOCAL3      (19<<3) /* reserved for local use */
        //#define LOG_LOCAL4      (20<<3) /* reserved for local use */
        //#define LOG_LOCAL5      (21<<3) /* reserved for local use */
        //#define LOG_LOCAL6      (22<<3) /* reserved for local use */
        //#define LOG_LOCAL7      (23<<3) /* reserved for local use */

        static string[] _facilityNames = {
            "KERN",
            "USER",
            "MAIL",
            "DAEMON",
            "AUTH",
            "SYSLOG",
            "LPR",
            "NEWS",
            "UUCP",
            "CRON",
            "AUTHPRIV",
            "FTP",
            "SYSTEM1",
            "SYSTEM2",
            "SYSTEM3",
            "SYSTEM4",
            "LOCAL0",
            "LOCAL1",
            "LOCAL2",
            "LOCAL3",
            "LOCAL4",
            "LOCAL5",
            "LOCAL6",
            "LOCAL7"
        };

        private static string _logDirectory = $@"{Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)}\Logs";

        // Key is now "Hostname_Date", e.g., "192_168_1_50_2023-10-27"
        private static readonly ConcurrentDictionary<string, RotatingLogger> _loggers = new ConcurrentDictionary<string, RotatingLogger>();

        private static readonly ConcurrentDictionary<string, string> _dnsCache = new ConcurrentDictionary<string, string>();

        public static async Task HandleIncomingMessages(CancellationToken ct) {

            if(Path.IsPathRooted(SyslogSettings.INSTANCE.LogDirectory)) {
                _logDirectory = SyslogSettings.INSTANCE.LogDirectory;
            } else {
                _logDirectory = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), SyslogSettings.INSTANCE.LogDirectory);
            }

            if(ct.IsCancellationRequested) {
                logger.Error("Task was canceled before it got started.");
                return;
            }

            UdpClient udpClient = null;

            try {
                logger.Info($"Files will be created in: {_logDirectory}");

                if(!Directory.Exists(_logDirectory)) {
                    Directory.CreateDirectory(_logDirectory);
                }
                logger.Info($"Rotation Size: {SyslogSettings.INSTANCE.RotateSizeMB} MB");
                logger.Info($"Rotation Count: {SyslogSettings.INSTANCE.RotateCount}");

                udpClient = new UdpClient(SyslogSettings.INSTANCE.Port);
                logger.Info($"Listening on port {SyslogSettings.INSTANCE.Port}...");

                while(true) {
                    ct.ThrowIfCancellationRequested();

                    UdpReceiveResult result = await udpClient.ReceiveAsync(ct);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    logger.Trace($"Packet '{message}' received from {result.RemoteEndPoint.Address}");

                    // 1. Get Hostname (e.g. "PC1" or "192_168_1_50")
                    string clientIdentifier = await GetHostnameAsync(result.RemoteEndPoint.Address,ct);

                    logger.Trace($"Resolved {result.RemoteEndPoint.Address} to {clientIdentifier}");

                    // 2. Get Current Date (e.g. "2023-10-27")
                    string datePart = DateTime.Now.ToString("yyyy-MM-dd");

                    // 3. Create Composite Key
                    // This creates a unique identifier for this host, for this specific day.
                    string dictionaryKey = $"{clientIdentifier.Replace('.','-')}_{datePart}";

                    string logEntry = DecodePacket(message);

                    if(string.IsNullOrWhiteSpace(logEntry)) {
                        logger.Warn($"Decoded log entry is empty for packet '{message}' from {clientIdentifier}. Skipping.");
                        continue;
                    }

                    // 4. Get/Create Logger based on the Composite Key
                    RotatingLogger rlogger = _loggers.GetOrAdd(dictionaryKey, CreateLoggerForKey);

                    await rlogger.WriteLogAsync(logEntry);
                    logger.Trace($"Logged message from {clientIdentifier} to {dictionaryKey}.log");
                }
            } catch(OperationCanceledException) {
                logger.Info("Cancellation requested. Exiting UDP listener.");
            } catch(Exception ex) {
                logger.Error(ex);
                logger.Debug(ex.StackTrace);
            } finally {
                udpClient?.Close();
            }
        }

        public static string DecodePacket(string log) {
            string result = null;
            try {
                int start = log.IndexOf('<'); // Ensure it starts with '<'
                int stop = log.IndexOf('>');  // Ensure it has a closing '>'
                if(start != 0 || stop <= start) {
                    logger.Warn($"Invalid syslog packet format: '{log}'. Missing or misplaced '<' and '>'.");
                    return null;
                }
                string pri_code = log.Substring(start + 1, stop - start - 1);
                uint pri = uint.Parse(pri_code);
                string message = log.Substring(stop + 1).Trim().TrimEnd('\n','\r');

                string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

                string[] msgParts = message.Split(new char[] { ' ' });

                if(msgParts.Length > 4) {
                    // Remove the first 3 parts (e.g., "Oct 27 14:23:01.567")
                    message = string.Join(" ", msgParts, 3, msgParts.Length - 3);
                }

                if(SyslogSettings.INSTANCE.ShowRemoteDateAndTime) {
                    if(msgParts.Length > 3) {
                        date = string.Join(" ", msgParts, 0, 3);
                    }
                }

                uint facility = (pri >> 3) & 0xff;
                uint severity = pri & 0x7;

                result = $"{date} {_severityNames[severity],-8} {_facilityNames[facility],-6} {message}";
            } catch(Exception ex) {
                logger.Warn($"Failed to decode packet '{log}': {ex.Message}");
            }
            return result;
        }


        // Accepts "Hostname_Date" and creates the file "Hostname_Date.log"
        private static RotatingLogger CreateLoggerForKey(string keyName) {
            // Sanitize just in case
            foreach(char c in Path.GetInvalidFileNameChars()) {
                keyName = keyName.Replace(c, '_');
            }

            string fileName = $"{keyName}.log";

            return new RotatingLogger(Path.Combine(_logDirectory, fileName), SyslogSettings.INSTANCE.RotateSizeMB * 1024 * 1024, SyslogSettings.INSTANCE.RotateCount);
        }

        private static async Task<string> GetHostnameAsync(IPAddress ip, CancellationToken token) {
            string ipKey = ip.ToString();
            string safeFallback = ipKey.Replace('.', '_');

            // 1. Check Cache first
            if(_dnsCache.TryGetValue(ipKey, out string cachedName)) {
                logger.Trace($"Cache hit for {ipKey}: {cachedName}");
                return cachedName;
            }

            // 2. Start the DNS task
            // Note: In older .NET, this task ignores the token and runs until completion or OS timeout.
            // We cannot stop it, but we can stop waiting for it.
            Task<IPHostEntry> dnsTask = Dns.GetHostEntryAsync(ip);

            // 3. Create a generic task that completes when the CancellationToken is cancelled
            var tcs = new TaskCompletionSource<bool>();
            using(token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs)) {
                // 4. Wait for whichever finishes first: The DNS result OR the Cancellation
                Task winner = await Task.WhenAny(dnsTask, tcs.Task);

                // If the cancellation task won, we return the fallback immediately
                if(winner == tcs.Task) {
                    // Optional: Cache the fallback so we don't retry a slow IP immediately
                    _dnsCache.TryAdd(ipKey, safeFallback);
                    logger.Trace($"DNS lookup for {ipKey} cancelled, using fallback.");
                    return safeFallback;
                }
            }

            // 5. If we get here, DNS finished successfully (or threw an error)
            try {
                // We await the task again to unwrap the result or exception
                IPHostEntry entry = await dnsTask;

                string hostname = entry.HostName;
                _dnsCache.TryAdd(ipKey, hostname);
                logger.Trace($"Resolved {ipKey} to {hostname}");
                return hostname;
            } catch(Exception) {
                // Handle SocketException (not found) or other errors
                _dnsCache.TryAdd(ipKey, safeFallback);
                logger.Trace($"Could not resolve hostname for {ipKey}, using fallback.");
                return safeFallback;
            }
        }

        private static async Task<string> GetHostnameAsync(IPAddress ip) {
            string ipKey = ip.ToString();

            if(_dnsCache.TryGetValue(ipKey, out string cachedName)) {
                return cachedName;
            }

            try {
                IPHostEntry entry = await Dns.GetHostEntryAsync(ip);
                string hostname = entry.HostName;
                _dnsCache.TryAdd(ipKey, hostname);
                return hostname;
            } catch(SocketException) {
                // Fallback: Replace dots with underscores
                string safeIp = ipKey.Replace('.', '_');
                _dnsCache.TryAdd(ipKey, safeIp);
                logger.Warn($"Could not resolve hostname for {ipKey}, using fallback.");
                return safeIp;
            }
        }
    }

    public class RotatingLogger {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly string _filePath;
        private readonly long _maxSizeBytes;
        private readonly int _maxBackups;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        public RotatingLogger(string filePath, long maxSizeBytes, int maxBackups) {
            _filePath = filePath;
            _maxSizeBytes = maxSizeBytes;
            _maxBackups = maxBackups;
        }

        public async Task WriteLogAsync(string text) {
            await _writeLock.WaitAsync();
            try {
                CheckAndRotate();
                using(StreamWriter writer = new StreamWriter(_filePath, append: true)) {
                    await writer.WriteLineAsync(text);
                    await writer.FlushAsync();
                }
            } catch(Exception ex) {
                logger.Error($"Error writing to log file {_filePath}: {ex.Message}");
            } finally {
                _writeLock.Release();
            }
        }

        private void CheckAndRotate() {
            FileInfo fi = new FileInfo(_filePath);
            if(!fi.Exists || fi.Length < _maxSizeBytes) return;

            for(int i = _maxBackups - 1; i >= 0; i--) {
                string source = (i == 0) ? _filePath : $"{_filePath}.{i}";
                string destination = $"{_filePath}.{i + 1}";

                if(File.Exists(source)) {
                    if(File.Exists(destination)) File.Delete(destination);
                    File.Move(source, destination);
                }
            }
        }
    }

    public static class UdpClientExtensions {
        // Adds a CancellationToken parameter to ReceiveAsync for older .NET versions
        public static async Task<UdpReceiveResult> ReceiveAsync(this UdpClient client, CancellationToken token) {
            // 1. Start the standard Receive task (cannot be stopped natively)
            Task<UdpReceiveResult> receiveTask = client.ReceiveAsync();

            // 2. If the token is essentially "None", just wait for the receive
            if(token == CancellationToken.None) {
                return await receiveTask;
            }

            // 3. Create a task that completes when the token is cancelled
            var tcs = new TaskCompletionSource<bool>();
            using(token.Register(() => tcs.TrySetResult(true))) {
                // 4. Wait for whichever finishes first: The Packet OR The Token
                Task winner = await Task.WhenAny(receiveTask, tcs.Task);

                // 5. If the token won the race, throw OperationCanceledException
                if(winner == tcs.Task) {
                    throw new OperationCanceledException(token);
                }
            }

            // 6. If we get here, the packet arrived. Return the result.
            return await receiveTask;
        }
    }

}
