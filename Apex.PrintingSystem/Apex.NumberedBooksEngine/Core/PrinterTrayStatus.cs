using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Apex.NumberedBooksEngine.Core
{
    /// <summary>
    /// Per-tray paper detection over SNMP (standard Printer-MIB), for network printers.
    ///
    /// Why this exists: when a numbering job feeds the original and the copy from two
    /// different trays and ONE tray runs dry, the printer firmware quietly substitutes the
    /// other (loaded) tray and keeps going — so the printer never reports "out of paper" and
    /// Windows/GDI has no way to stop it. But the empty tray is still physically empty, and
    /// the Printer-MIB reports its sheet level directly. We read each input tray's
    /// <c>prtInputCurrentLevel</c> (OID 1.3.6.1.2.1.43.8.2.1.10.1.N): a value of exactly 0
    /// means that tray is empty, which lets the run hold until the operator refills it.
    ///
    /// <b>Fail-open by design.</b> This only ever returns true when it POSITIVELY parses a
    /// well-formed SNMP reply reporting a tray at level 0. Any doubt — a non-network printer,
    /// a timeout, SNMP disabled, a malformed packet, an unknown level (-1/-2/-3) — returns
    /// false, so a healthy run is never paused by a parsing mistake. The worst a bug can do is
    /// miss an empty tray (today's behaviour), never invent one.
    /// </summary>
    internal static class PrinterTrayStatus
    {
        // prtInputCurrentLevel, 1.3.6.1.2.1.43.8.2.1.10.1.N  (encoded arcs after 1.3 = 0x2b)
        private static readonly byte[] InputLevelPrefix =
            { 0x2b, 0x06, 0x01, 0x02, 0x01, 0x2b, 0x08, 0x02, 0x01, 0x0a, 0x01 };

        /// <summary>
        /// True only when SNMP positively reports at least one input tray empty (level 0).
        /// False on anything else. Never throws. <paramref name="host"/> is the printer's
        /// IP or hostname; indices 1..8 cover ordinary multi-tray printers.
        /// </summary>
        public static bool AnyInputTrayEmpty(string? host, string community = "public", int timeoutMs = 600)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            try
            {
                if (!TryResolve(host!, out var addr)) return false;

                using var udp = new UdpClient(addr.AddressFamily);
                udp.Client.ReceiveTimeout = timeoutMs;
                var ep = new IPEndPoint(addr, 161);

                // Ask a handful of tray indices in one burst, then read whatever answers.
                for (byte n = 1; n <= 8; n++)
                {
                    var pkt = BuildGet(community, (byte)(0x10 + n), AppendIndex(n));
                    try { udp.Send(pkt, pkt.Length, ep); } catch { }
                }

                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                var any = IPAddress.Any;
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var remote = new IPEndPoint(any, 0);
                        var resp = udp.Receive(ref remote);
                        if (TryParseLevel(resp, out int level) && level == 0)
                            return true;   // a tray is empty — positive, hold the run
                    }
                    catch (SocketException) { break; }   // timeout → stop reading
                    catch { /* ignore a malformed datagram, keep reading */ }
                }
                return false;
            }
            catch
            {
                return false;   // fail-open: never pause on an SNMP error
            }
        }

        private static byte[] AppendIndex(byte n)
        {
            var oid = new byte[InputLevelPrefix.Length + 1];
            Array.Copy(InputLevelPrefix, oid, InputLevelPrefix.Length);
            oid[^1] = n;   // tray index (1..8, each < 128 → single arc byte)
            return oid;
        }

        private static bool TryResolve(string host, out IPAddress addr)
        {
            if (IPAddress.TryParse(host, out addr!)) return true;
            try
            {
                // A print port host is normally an IP or a short LAN name — a quick lookup.
                var list = Dns.GetHostAddresses(host);
                foreach (var a in list)
                    if (a.AddressFamily == AddressFamily.InterNetwork) { addr = a; return true; }
                if (list.Length > 0) { addr = list[0]; return true; }
            }
            catch { }
            addr = IPAddress.None;
            return false;
        }

        // ── Minimal SNMPv1 GET (all lengths short-form, < 128) ──────────────────────────
        private static byte[] BuildGet(string community, byte requestId, byte[] oid)
        {
            var comm = System.Text.Encoding.ASCII.GetBytes(community ?? "public");

            var varbind = Tlv(0x30, Concat(
                Tlv(0x06, oid),
                new byte[] { 0x05, 0x00 }));            // value = NULL
            var varbindList = Tlv(0x30, varbind);
            var pdu = Tlv(0xA0, Concat(                  // GetRequest-PDU
                new byte[] { 0x02, 0x01, requestId },    // request-id
                new byte[] { 0x02, 0x01, 0x00 },         // error-status
                new byte[] { 0x02, 0x01, 0x00 },         // error-index
                varbindList));
            return Tlv(0x30, Concat(
                new byte[] { 0x02, 0x01, 0x00 },         // version = 0 (v1)
                Tlv(0x04, comm),                         // community
                pdu));
        }

        /// <summary>Parses the single varbind's INTEGER level from a GetResponse. Returns false
        /// unless the reply is well-formed with error-status 0 and an integer value.</summary>
        private static bool TryParseLevel(byte[] msg, out int level)
        {
            level = 0;
            try
            {
                int p = 0;
                if (!ReadHeader(msg, ref p, 0x30, out int _)) return false;     // message SEQUENCE
                if (!SkipTlv(msg, ref p, 0x02)) return false;                   // version
                if (!SkipTlv(msg, ref p, 0x04)) return false;                   // community
                if (!ReadHeader(msg, ref p, 0xA2, out int _)) return false;     // GetResponse-PDU
                if (!SkipTlv(msg, ref p, 0x02)) return false;                   // request-id
                if (!ReadHeader(msg, ref p, 0x02, out int esLen)) return false; // error-status
                int errStatus = ReadIntBody(msg, ref p, esLen);
                if (errStatus != 0) return false;                              // noSuchName etc.
                if (!SkipTlv(msg, ref p, 0x02)) return false;                   // error-index
                if (!ReadHeader(msg, ref p, 0x30, out int _)) return false;     // varbind-list
                if (!ReadHeader(msg, ref p, 0x30, out int _)) return false;     // varbind
                if (!SkipTlv(msg, ref p, 0x06)) return false;                   // OID
                if (!ReadHeader(msg, ref p, 0x02, out int vLen)) return false;  // value INTEGER
                level = ReadIntBody(msg, ref p, vLen);
                return true;
            }
            catch { return false; }
        }

        // ── tiny BER helpers ────────────────────────────────────────────────────────────
        private static bool ReadHeader(byte[] b, ref int p, byte expectTag, out int len)
        {
            len = 0;
            if (p + 2 > b.Length || b[p] != expectTag) return false;
            p++;
            int l = b[p++];
            if (l >= 0x80)   // long-form length (rare for these small values) — be tolerant
            {
                int n = l & 0x7f;
                if (n == 0 || n > 4 || p + n > b.Length) return false;
                l = 0;
                for (int i = 0; i < n; i++) l = (l << 8) | b[p++];
            }
            len = l;
            return p + len <= b.Length;
        }

        private static bool SkipTlv(byte[] b, ref int p, byte expectTag)
        {
            if (!ReadHeader(b, ref p, expectTag, out int len)) return false;
            p += len;
            return true;
        }

        private static int ReadIntBody(byte[] b, ref int p, int len)
        {
            if (len <= 0 || p + len > b.Length) { p += Math.Max(0, len); return int.MinValue; }
            int v = (sbyte)b[p];           // sign-extend the first byte
            for (int i = 1; i < len; i++) v = (v << 8) | b[p + i];
            p += len;
            return v;
        }

        private static byte[] Tlv(byte tag, byte[] content)
        {
            // All payloads here are < 128 bytes, so a single-byte short-form length is valid.
            var outb = new byte[2 + content.Length];
            outb[0] = tag;
            outb[1] = (byte)content.Length;
            Array.Copy(content, 0, outb, 2, content.Length);
            return outb;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (var p in parts) total += p.Length;
            var outb = new byte[total];
            int o = 0;
            foreach (var p in parts) { Array.Copy(p, 0, outb, o, p.Length); o += p.Length; }
            return outb;
        }
    }
}
