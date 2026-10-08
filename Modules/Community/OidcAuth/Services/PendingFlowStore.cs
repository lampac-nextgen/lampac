using System;
using System.Collections.Concurrent;

namespace OidcAuth.Services
{
    internal class PendingFlow
    {
        /// <summary>Temp device uid from /oidc/login?uid= (does not affect identity)</summary>
        public string? DeviceUid { get; set; }

        public string Nonce { get; set; } = "";

        public string CodeVerifier { get; set; } = "";

        /// <summary>Local path only, validated on creation</summary>
        public string ReturnUrl { get; set; } = "/";

        /// <summary>QR-login code this flow should bind to</summary>
        public string? LinkCode { get; set; }

        public DateTimeOffset Created { get; set; }
    }

    /// <summary>
    /// In-process store of pending OIDC flows: state -> flow data.
    /// Single-use, TTL 10 minutes. Server restart discards unfinished logins.
    /// </summary>
    internal static class PendingFlowStore
    {
        const int LifetimeMinutes = 10;

        static readonly ConcurrentDictionary<string, PendingFlow> _flows = new();
        static DateTimeOffset _nextSweep = DateTimeOffset.MinValue;

        public static void Add(string state, PendingFlow flow)
        {
            Sweep();
            flow.Created = DateTimeOffset.Now;
            _flows[state] = flow;
        }

        /// <summary>Atomically takes and removes the flow; null if unknown or expired</summary>
        public static PendingFlow? Take(string state)
        {
            if (string.IsNullOrEmpty(state))
                return null;

            if (!_flows.TryRemove(state, out var flow) || flow == null)
                return null;

            if (flow.Created.AddMinutes(LifetimeMinutes) < DateTimeOffset.Now)
                return null;

            return flow;
        }

        static void Sweep()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (_nextSweep > now)
                return;

            _nextSweep = now.AddMinutes(1);

            foreach (var kv in _flows)
            {
                if (kv.Value.Created.AddMinutes(LifetimeMinutes) < now)
                    _flows.TryRemove(kv.Key, out _);
            }
        }
    }
}
