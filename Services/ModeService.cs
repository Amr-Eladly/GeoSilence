using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GeoSilence.Models;

namespace GeoSilence.Services
{
    public class ModeService
    {
        public void SetMode(ModeType mode)
        {
            // For now 
            Console.WriteLine($"Switching to {mode}");

            // Later
            // Android → AudioManager
            // iOS → Notification
        }
    }
}
