using System;

namespace TrainerScheduler.Services
{
    public static partial class LicenseService
    {
        /// <summary>
        /// </summary>
        public static bool TryActivateFromSerial(string serial, out string message)
        {
            var ok = ActivateWithSerial(serial, out message);

            if (ok)
            {
                LoadAndValidate();
            }

            return ok;
        }
    }
}
