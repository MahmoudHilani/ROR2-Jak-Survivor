using System;

namespace JakSurvivor
{
    // A finite wave never evicts a victim. Reaching the explicit safety bound stops
    // acquiring additional victims rather than allowing an old target to be hit twice.
    internal sealed class JakWaveVictims
    {
        internal const int Capacity = 2048;
        private int[] ids;
        internal int Count { get; private set; }
        internal bool Full => Count >= Capacity;
        internal bool Contains(int id)
        {
            if (id == 0 || ids == null) return false;
            int index = (id & int.MaxValue) % ids.Length;
            while (ids[index] != 0)
            {
                if (ids[index] == id) return true;
                index = (index + 1) % ids.Length;
            }
            return false;
        }
        internal bool Add(int id)
        {
            if (id == 0) return false; // Unity instance IDs are nonzero.
            if (ids == null) ids = new int[Capacity * 2];
            int index = (id & int.MaxValue) % ids.Length;
            while (ids[index] != 0)
            {
                if (ids[index] == id) return false;
                index = (index + 1) % ids.Length;
            }
            if (Count >= Capacity) return false;
            ids[index] = id;
            Count++;
            return true;
        }
        internal void Clear()
        {
            if (Count == 0) return;
            Count = 0;
            Array.Clear(ids, 0, ids.Length);
        }
    }
}
