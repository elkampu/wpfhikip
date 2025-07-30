using System.Linq;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Comprehensive mDNS constants for maximum discovery coverage
    /// </summary>
    public static class MdnsConstants
    {
        // mDNS multicast configuration
        public const string MulticastAddress = "224.0.0.251";
        public const int MulticastPort = 5353;

        // Core discovery queries (highest priority)
        public static readonly string[] CoreServices =
        {
            "_services._dns-sd._udp.local.",
            "_http._tcp.local.",
            "_https._tcp.local.",
            "_device-info._tcp.local.",
            "_workstation._tcp.local.",
            "_domain._udp.local."
        };

        // Security & Camera services (primary target)
        public static readonly string[] SecurityServices =
        {
            "_onvif._tcp.local.",
            "_camera._tcp.local.",
            "_rtsp._tcp.local.",
            "_psia._tcp.local.",
            "_axis-video._tcp.local.",
            "_axis-nvr._tcp.local.",
            "_hikvision._tcp.local.",
            "_dahua._tcp.local.",
            "_bosch._tcp.local.",
            "_samsung._tcp.local.",
            "_pelco._tcp.local.",
            "_genetec._tcp.local.",
            "_milestone._tcp.local.",
            "_avigilon._tcp.local.",
            "_vivotek._tcp.local.",
            "_acti._tcp.local.",
            "_mobotix._tcp.local.",
            "_panasonic._tcp.local.",
            "_sony._tcp.local.",
            "_canon._tcp.local.",
            "_ipcam._tcp.local.",
            "_webcam._tcp.local.",
            "_nvr._tcp.local.",
            "_dvr._tcp.local.",
            "_cctv._tcp.local.",
            "_surveillance._tcp.local.",
            "_security._tcp.local.",
            "_doorbell._tcp.local.",
            "_intercom._tcp.local."
        };

        // Network infrastructure services
        public static readonly string[] NetworkServices =
        {
            "_ssh._tcp.local.",
            "_telnet._tcp.local.",
            "_ftp._tcp.local.",
            "_sftp._tcp.local.",
            "_smb._tcp.local.",
            "_nfs._tcp.local.",
            "_tftp._udp.local.",
            "_snmp._udp.local.",
            "_syslog._udp.local.",
            "_dns._udp.local.",
            "_dhcp._udp.local.",
            "_ntp._udp.local.",
            "_ldap._tcp.local.",
            "_kerberos._udp.local.",
            "_router._tcp.local.",
            "_switch._tcp.local.",
            "_firewall._tcp.local.",
            "_proxy._tcp.local.",
            "_vpn._tcp.local.",
            "_radius._udp.local.",
            "_tacacs._tcp.local."
        };

        // Media & streaming services
        public static readonly string[] MediaServices =
        {
            "_airplay._tcp.local.",
            "_raop._tcp.local.",
            "_googlecast._tcp.local.",
            "_chromecast._tcp.local.",
            "_upnp._tcp.local.",
            "_dlna._tcp.local.",
            "_roku._tcp.local.",
            "_appletv._tcp.local.",
            "_airserver._tcp.local.",
            "_miracast._tcp.local.",
            "_plex._tcp.local.",
            "_emby._tcp.local.",
            "_jellyfin._tcp.local.",
            "_kodi._tcp.local.",
            "_xbmc._tcp.local.",
            "_spotify._tcp.local.",
            "_sonos._tcp.local.",
            "_homekit._tcp.local.",
            "_hap._tcp.local.",
            "_matter._tcp.local.",
            "_thread._udp.local."
        };

        // Printer & document services
        public static readonly string[] PrinterServices =
        {
            "_printer._tcp.local.",
            "_ipp._tcp.local.",
            "_ipps._tcp.local.",
            "_escl._tcp.local.",
            "_uscan._tcp.local.",
            "_scanner._tcp.local.",
            "_pdl-datastream._tcp.local.",
            "_cups._tcp.local.",
            "_print-caps._tcp.local.",
            "_universal._sub._ipp._tcp.local.",
            "_hp-smart._tcp.local.",
            "_canon-bjnp._udp.local.",
            "_epson-escp._tcp.local.",
            "_brother._tcp.local.",
            "_lexmark._tcp.local."
        };

        // Industrial & IoT devices
        public static readonly string[] IndustrialServices =
        {
            "_modbus._tcp.local.",
            "_bacnet._udp.local.",
            "_opcua._tcp.local.",
            "_mqtt._tcp.local.",
            "_coap._udp.local.",
            "_zigbee._udp.local.",
            "_zwave._udp.local.",
            "_lora._udp.local.",
            "_lorawan._udp.local.",
            "_6lowpan._udp.local.",
            "_plc._tcp.local.",
            "_scada._tcp.local.",
            "_hmi._tcp.local.",
            "_industrial._tcp.local.",
            "_automation._tcp.local.",
            "_sensor._tcp.local.",
            "_actuator._tcp.local.",
            "_controller._tcp.local."
        };

        // Gaming & entertainment
        public static readonly string[] GamingServices =
        {
            "_xbox._tcp.local.",
            "_playstation._tcp.local.",
            "_nintendo._tcp.local.",
            "_steam._tcp.local.",
            "_gamestream._tcp.local.",
            "_nvidia._tcp.local.",
            "_parsec._tcp.local.",
            "_moonlight._tcp.local.",
            "_virtualhere._tcp.local."
        };

        // Development & debugging services
        public static readonly string[] DevelopmentServices =
        {
            "_adb._tcp.local.",
            "_debug._tcp.local.",
            "_gdb._tcp.local.",
            "_lldb._tcp.local.",
            "_devtools._tcp.local.",
            "_livereload._tcp.local.",
            "_webpack._tcp.local.",
            "_nodejs._tcp.local.",
            "_dotnet._tcp.local.",
            "_java._tcp.local.",
            "_python._tcp.local.",
            "_ruby._tcp.local.",
            "_php._tcp.local.",
            "_mysql._tcp.local.",
            "_postgresql._tcp.local.",
            "_mongodb._tcp.local.",
            "_redis._tcp.local.",
            "_elasticsearch._tcp.local.",
            "_grafana._tcp.local.",
            "_prometheus._tcp.local."
        };

        // Storage & NAS services
        public static readonly string[] StorageServices =
        {
            "_nas._tcp.local.",
            "_storage._tcp.local.",
            "_iscsi._tcp.local.",
            "_afp._tcp.local.",
            "_adisk._tcp.local.",
            "_timemachine._tcp.local.",
            "_synology._tcp.local.",
            "_qnap._tcp.local.",
            "_drobo._tcp.local.",
            "_netgear._tcp.local.",
            "_wd._tcp.local.",
            "_seagate._tcp.local.",
            "_buffalo._tcp.local.",
            "_dlink._tcp.local.",
            "_asustor._tcp.local."
        };

        // Communication services
        public static readonly string[] CommunicationServices =
        {
            "_sip._tcp.local.",
            "_sip._udp.local.",
            "_h323._tcp.local.",
            "_h323._udp.local.",
            "_rtp._udp.local.",
            "_rtcp._udp.local.",
            "_xmpp._tcp.local.",
            "_jabber._tcp.local.",
            "_irc._tcp.local.",
            "_mumble._tcp.local.",
            "_teamspeak._udp.local.",
            "_discord._tcp.local.",
            "_skype._tcp.local.",
            "_zoom._tcp.local.",
            "_teams._tcp.local.",
            "_webex._tcp.local.",
            "_gotomeeting._tcp.local."
        };

        // Generic device discovery patterns
        public static readonly string[] GenericServices =
        {
            "_tcp.local.",
            "_udp.local.",
            "_device._tcp.local.",
            "_service._tcp.local.",
            "_server._tcp.local.",
            "_client._tcp.local.",
            "_api._tcp.local.",
            "_rest._tcp.local.",
            "_soap._tcp.local.",
            "_web._tcp.local.",
            "_admin._tcp.local.",
            "_config._tcp.local.",
            "_management._tcp.local.",
            "_monitor._tcp.local.",
            "_status._tcp.local.",
            "_health._tcp.local."
        };

        /// <summary>
        /// Gets priority-ordered services for phased discovery
        /// </summary>
        public static string[][] GetServicesByPriority()
        {
            return new[]
            {
                CoreServices,           // Phase 1: Essential services
                SecurityServices,       // Phase 2: Primary target (cameras/security)
                NetworkServices,        // Phase 3: Network infrastructure
                StorageServices,        // Phase 4: Storage devices
                MediaServices,          // Phase 5: Media/streaming devices
                PrinterServices,        // Phase 6: Printers/scanners
                IndustrialServices,     // Phase 7: Industrial/IoT
                CommunicationServices,  // Phase 8: Communication systems
                DevelopmentServices,    // Phase 9: Development tools
                GamingServices,         // Phase 10: Gaming devices
                GenericServices         // Phase 11: Generic patterns
            };
        }

        /// <summary>
        /// Gets all service types for comprehensive scanning
        /// </summary>
        public static string[] GetAllServices()
        {
            return CoreServices
                .Concat(SecurityServices)
                .Concat(NetworkServices)
                .Concat(StorageServices)
                .Concat(MediaServices)
                .Concat(PrinterServices)
                .Concat(IndustrialServices)
                .Concat(CommunicationServices)
                .Concat(DevelopmentServices)
                .Concat(GamingServices)
                .Concat(GenericServices)
                .ToArray();
        }

        /// <summary>
        /// Gets security-focused services for camera discovery
        /// </summary>
        public static string[] GetSecurityFocusedServices()
        {
            return CoreServices
                .Concat(SecurityServices)
                .Concat(NetworkServices.Take(8)) // Include basic network services
                .ToArray();
        }

        /// <summary>
        /// Gets lightweight service set for quick discovery
        /// </summary>
        public static string[] GetLightweightServices()
        {
            return CoreServices
                .Concat(SecurityServices.Take(10))
                .Concat(NetworkServices.Take(5))
                .ToArray();
        }
    }
}