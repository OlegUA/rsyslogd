using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace RSCVCommon {
    /// <summary>
    /// Abstract calls implements CRU operations on JSON config file
    /// </summary>
    public abstract class RSCVSettings {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        /// <value>JSON configuration file name. Must be implemented in the derived class</value>
        protected abstract string ConfigName { get; set; }
        /// <summary>
        /// Load JSON and populate content to object presents configuration data. If configuration file doesn't exist will create with defaults.
        /// </summary>
        public virtual void Load() {
            if(!File.Exists(CurrentPath() + ConfigName)) {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(CurrentPath() + ConfigName, json);
                return;
            }
            try {
                JsonConvert.PopulateObject(File.ReadAllText(CurrentPath() + ConfigName), this, new JsonSerializerSettings {
                    MissingMemberHandling = MissingMemberHandling.Error
                });
            } catch(JsonSerializationException ex) {
                logger.Error($"{ConfigName} - {ex.Message}");
            } catch(Exception ex) {
                logger.Error($"{ConfigName} - {ex.Message}");
                Save();
            }
        }
        /// <summary>
        /// Serialize object presents configuration data to JSON
        /// </summary>
        public virtual void Save() {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(CurrentPath() + ConfigName, json);
        }
        /// <summary>
        /// Determinate the current path where application is executing
        /// </summary>
        /// <returns>absolute file path</returns>
        public string CurrentPath() {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        }
        /// <summary>
        /// Service method
        /// </summary>
        /// <returns>serialized JSON string of config object</returns>
        public string GetJson() {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}
