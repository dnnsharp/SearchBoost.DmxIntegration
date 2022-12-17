using Bring2mind.DNN.Modules.DMX.Entities.Entries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DnnSharp.SearchBoost.DmxIntegration.Utils {
    public class EntryComparer : IEqualityComparer<EntryInfo> {

        public bool Equals(EntryInfo x, EntryInfo y) {
            return x.EntryId == y.EntryId;
        }

        public int GetHashCode(EntryInfo obj) {
            return obj.EntryId;
        }
    }
}
