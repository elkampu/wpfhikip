using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Models
{
    public class CameraSettings
    {
        public string? IPAddress { get; set; }
        public string? SubnetMask { get; set; }
        public string? DefaultGateway { get; set; }
        public string? DNS1 { get; set; }
        public string? DNS2 { get; set; }

        public bool NTPEnabled { get; set; }
        public bool NTPSynced { get; set; }
        public string? NTPServer { get; set; }
        public string? TimeZone { get; set; }
        public string? DSTEnabled { get; set; }
        public string? DSTSettings { get; set; }

    }
}
