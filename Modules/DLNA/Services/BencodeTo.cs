using MonoTorrent;
using System;
using System.Collections.Generic;
using System.Text;

namespace DLNA;

public static class BencodeTo
{
    public static string Magnet(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        try
        {
            return Magnet(Torrent.Load(data));
        }
        catch
        {
            return null;
        }
    }

    public static string Magnet(Torrent torrent)
    {
        if (torrent?.InfoHashes?.V1 == null)
            return null;

        var sb = new StringBuilder(256);

        sb.Append("magnet:?xt=urn:btih:")
          .Append(torrent.InfoHashes.V1.ToHex().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(torrent.Name))
        {
            sb.Append("&dn=")
              .Append(Uri.EscapeDataString(torrent.Name));
        }

        if (torrent.Size > 0)
        {
            sb.Append("&xl=")
              .Append(torrent.Size);
        }

        var added = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tier in torrent.AnnounceUrls)
        {
            if (tier == null)
                continue;

            foreach (string tracker in tier)
            {
                string value = tracker?.Trim();

                if (string.IsNullOrEmpty(value) || !added.Add(value))
                    continue;

                sb.Append("&tr=")
                  .Append(Uri.EscapeDataString(value));
            }
        }

        return sb.ToString();
    }
}
