using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WowAddonUpdater.Models;

namespace WowAddonUpdater.Services
{
    public class AutoScanCompletedEventArgs : EventArgs
    {
        public List<Addon> UpdatedAddons { get; set; }
        public int AddonsUpdated { get; set; }
        public bool AutoUpdateWasPerformed { get; set; }
        public string Message { get; set; }

        public AutoScanCompletedEventArgs(List<Addon> updatedAddons, int addonsUpdated, bool autoUpdatePerformed, string message = "")
        {
            UpdatedAddons = updatedAddons;
            AddonsUpdated = addonsUpdated;
            AutoUpdateWasPerformed = autoUpdatePerformed;
            Message = message;
        }
    }

    public interface IAutoScanService : IDisposable
    {
        /// <summary>
        /// Event som triggas när auto scan har slutförts
        /// </summary>
        event EventHandler<AutoScanCompletedEventArgs> AutoScanCompleted;

        /// <summary>
        /// Startar auto scan service med aktuella inställningar från config
        /// </summary>
        void Start();

        /// <summary>
        /// Stoppar auto scan service
        /// </summary>
        void Stop();

        /// <summary>
        /// Uppdaterar konfigurationen och startar om timer om nödvändigt
        /// </summary>
        void RefreshConfiguration();

        /// <summary>
        /// Kontrollerar om auto scan service är aktiv
        /// </summary>
        bool IsRunning { get; }
    }
}