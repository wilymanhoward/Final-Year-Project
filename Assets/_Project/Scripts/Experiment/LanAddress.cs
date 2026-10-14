using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FYP.Detective
{
    /// <summary>
    /// IPv4 helpers for the direct headset-to-headset connection: validating the host address typed into the
    /// config, and finding this device's LAN address so the host can show it. Selection logic is pure
    /// (<see cref="Pick"/>) so it can be unit tested; <see cref="FindLocalIpv4"/> only gathers the candidates.
    /// </summary>
    public static class LanAddress
    {
        /// <summary>Strict dotted IPv4 ("192.168.1.20"): 4 parts, 0-255, digits only. Trims spaces.</summary>
        public static bool TryParseIpv4(string text, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split('.');
            if (parts.Length != 4) return false;
            var values = new int[4];
            for (int i = 0; i < 4; i++)
            {
                string p = parts[i];
                if (p.Length == 0 || p.Length > 3) return false;
                foreach (char c in p) if (c < '0' || c > '9') return false;
                values[i] = int.Parse(p, CultureInfo.InvariantCulture);
                if (values[i] > 255) return false;
            }
            normalized = string.Join(".", values);
            return true;
        }

        /// <summary>10/8, 172.16/12, 192.168/16 (where a headset on Wi-Fi or a phone hotspot normally is).</summary>
        public static bool IsPrivate(string ipv4)
        {
            if (!TryParseIpv4(ipv4, out string n)) return false;
            string[] p = n.Split('.');
            int a = int.Parse(p[0], CultureInfo.InvariantCulture), b = int.Parse(p[1], CultureInfo.InvariantCulture);
            return a == 10 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168);
        }

        /// <summary>
        /// Best address to show: a private address on a Wi-Fi interface ("wlan*") first, then any private address,
        /// then any non-loopback, non-link-local address. Null if there is none.
        /// </summary>
        public static string Pick(IEnumerable<(string interfaceName, string ipv4)> candidates)
        {
            string anyPrivate = null, anyOther = null;
            foreach (var (name, ip) in candidates)
            {
                if (!TryParseIpv4(ip, out string n) || n.StartsWith("127.", StringComparison.Ordinal)
                    || n.StartsWith("169.254.", StringComparison.Ordinal) || n == "0.0.0.0") continue;
                if (IsPrivate(n))
                {
                    if (name != null && name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase)) return n;
                    if (anyPrivate == null) anyPrivate = n;
                }
                else if (anyOther == null) anyOther = n;
            }
            return anyPrivate ?? anyOther;
        }

        /// <summary>This device's LAN IPv4 address, or null (no network / not allowed to enumerate).</summary>
        public static string FindLocalIpv4()
        {
            var candidates = new List<(string, string)>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (UnicastIPAddressInformation a in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) candidates.Add((ni.Name, a.Address.ToString()));
                    }
                }
            }
            catch (Exception) { /* some Android builds refuse enumeration; fall back below */ }

            string picked = Pick(candidates);
            if (picked != null) return picked;

            // Fallback: the address the OS would use to reach the internet. UDP "connect" sends nothing.
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
                    return Pick(new[] { ("route", ((IPEndPoint)s.LocalEndPoint).Address.ToString()) });
                }
            }
            catch (Exception) { return null; }
        }
    }
}
