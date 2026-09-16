using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TrainerScheduler.Network;

public static class LanNetworkHelper
{
    public static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        var result = new List<string>();

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            // skip loopback/tunnel
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var ipProps = ni.GetIPProperties();
            foreach (var uni in ipProps.UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var ip = uni.Address;
                if (IPAddress.IsLoopback(ip))
                    continue;

                result.Add(ip.ToString());
            }
        }

        return result.Distinct().OrderBy(x => x).ToList();
    }
}
