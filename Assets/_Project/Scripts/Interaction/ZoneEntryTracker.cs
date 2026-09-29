namespace FYP.Detective
{
    /// <summary>
    /// Counts ENTRIES into one zone (outside -> inside transitions), not frames spent inside.
    /// An entry within <see cref="CooldownSeconds"/> of the last counted entry is ignored,
    /// so jitter at the zone edge doesn't produce several errors. Pure logic; the caller
    /// supplies "inside?" and the time each frame.
    /// </summary>
    public sealed class ZoneEntryTracker
    {
        private double _lastCountedTime;
        private bool _hasCounted;

        public double CooldownSeconds { get; set; }
        public bool IsInside { get; private set; }
        public int EntryCount { get; private set; }

        public ZoneEntryTracker(double cooldownSeconds = 1.0)
        {
            CooldownSeconds = cooldownSeconds;
        }

        /// <summary>Feed the current state. Returns true if this call counted a new entry.</summary>
        public bool Update(bool inside, double now)
        {
            bool counted = false;
            if (inside && !IsInside)
            {
                if (!_hasCounted || now - _lastCountedTime >= CooldownSeconds)
                {
                    EntryCount++;
                    _lastCountedTime = now;
                    _hasCounted = true;
                    counted = true;
                }
            }
            IsInside = inside;
            return counted;
        }

        public void Reset()
        {
            IsInside = false;
            EntryCount = 0;
            _hasCounted = false;
            _lastCountedTime = 0;
        }
    }
}
