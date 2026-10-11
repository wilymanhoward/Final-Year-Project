namespace FYP.Detective
{
    /// <summary>
    /// Filters one tracked point (head or a hand) before it is tested against an exclusion zone:
    /// while the point is not tracked it is ignored (never "inside"), and when tracking comes back with the
    /// point already inside the zone it stays ignored until it has been seen outside once. So losing and
    /// regaining tracking can never create a contamination "entry" by itself. Pure logic, one per point per zone.
    /// </summary>
    public sealed class TrackedPointGate
    {
        private bool _wasTracked;
        private bool _suppressed;

        /// <summary>Feed tracking + raw "inside?" each frame. Returns whether the point counts as inside.</summary>
        public bool Update(bool tracked, bool inside)
        {
            if (!tracked)
            {
                _wasTracked = false;
                return false;
            }
            if (!_wasTracked)
            {
                // First observation after (re)gaining tracking: if it is already inside, it didn't "enter".
                _wasTracked = true;
                _suppressed = inside;
            }
            if (_suppressed && !inside) _suppressed = false;
            return inside && !_suppressed;
        }

        public void Reset()
        {
            _wasTracked = false;
            _suppressed = false;
        }
    }
}
