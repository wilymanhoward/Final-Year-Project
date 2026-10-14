using System.Collections.Generic;

namespace FYP.Detective
{
    /// <summary>
    /// Routes game-state changes in the team condition. <see cref="SessionManager"/> sends every request here while
    /// <see cref="IsActive"/> (connected to a partner); the implementation (FYP.Detective.Net) applies it on the host
    /// and mirrors it to the client, which applies it when the host's message arrives. When inactive (solo run, or no
    /// partner) SessionManager applies requests locally, exactly as before. Keeps the core assembly free of Netcode.
    /// </summary>
    public interface ICaseAuthority
    {
        /// <summary>True while a partner is connected, so requests go through the host.</summary>
        bool IsActive { get; }

        void RequestStart();
        void RequestEnd(SessionEndReason reason);
        void RequestClueFound(string clueId, string by);
        void RequestClueTagged(string clueId, bool tagged, string by);
        void RequestSubmit(string suspectId, IReadOnlyList<string> evidenceIds, string by);

        /// <summary>This headset counted a contamination entry of its own body; tell the partner.</summary>
        void ReportContamination(string zoneId, BodyPart part, string by);
    }
}
