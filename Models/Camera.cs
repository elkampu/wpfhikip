using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Models
{
    public class Camera

    {

        public CameraProtocol Protocol { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? Firmware { get; set; }
        public string? SerialNumber { get; set; }
        public string? MACAddress { get; set; }


        public CameraConnection Connection { get; set; }
        public CameraSettings Settings { get; set; }
        public CameraVideoStream VideoStream { get; set; }
    }
}
