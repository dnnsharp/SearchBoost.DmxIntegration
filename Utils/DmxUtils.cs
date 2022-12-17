using Bring2mind.DNN.Modules.DMX;
using Bring2mind.DNN.Modules.DMX.Entities.Entries;
using Bring2mind.DNN.Modules.DMX.Services.Storage;
using DnnSharp.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DnnSharp.SearchBoost.DmxIntegration.Utils {
    public static class DmxUtils {

        public static IEnumerable<EntryInfo> GetDmxFilesForAdmin(int portalId, int collectionId, bool includeSubfolders) {
            List<EntryInfo> toReturn = new List<EntryInfo>();
            IEnumerable<EntryInfo> entries = GetDmxEntriesWithAllData(portalId, 1, true, collectionId);
            toReturn.AddRange(entries.Where(x => x.IsFile));
            if (includeSubfolders) {
                foreach (EntryInfo entry in entries) {
                    if (entry.IsCollection) {
                        toReturn.AddRange(GetDmxFilesForAdmin(portalId, entry.EntryId, true));
                    }
                }
            }
            return toReturn;
        }

        /// <summary>
        /// Get DMX entries (skipping deleted and hidden ones)
        /// </summary>
        /// <param name="collectionId">Collection id from dmx</param>
        /// <returns></returns>
        public static IEnumerable<EntryInfo> GetDmxEntriesWithAllData(int portalId, int userId, bool userIsAdmin, int collectionId) {
            int total = -1;
            return API.GetFolderContents(portalId, collectionId, userId, userIsAdmin, CultureInfo.CurrentCulture.ToString(), true, true, false, false, "", ref total, 0, -1);
        }

        /// <summary>
        /// Return collections (dmx folders) for specified portal & collection
        /// </summary>
        /// <param name="portalId"></param>
        /// <param name="collectionId"></param>
        /// <returns>A tuple (title, id, path). Path is in format 0;collectionId;subCollectionId;</returns>
        public static List<Tuple<string, int, string>> GetCollections(int portalId, int collectionId) {
            List<Tuple<string, int, string>> toReturn = new List<Tuple<string, int, string>>();

            if (ReflectionUtil.IsAssemblyPresent("Bring2mind.DNN.Modules.DMX.Core") == false)
                return toReturn;

            IEnumerable<EntryInfo> entries = GetDmxEntriesWithAllData(portalId, 1, true, collectionId);

            return entries.Where(x => x.IsCollection).Select(x => new Tuple<string, int, string>(x.Title, x.EntryId, x.Path)).ToList();
        }

        /// <summary>
        /// Get entry data without passing through security and without logging
        /// </summary>
        /// <param name="entry"></param>
        /// <returns>Returns null if can not get data</returns>
        public static Stream GetDataForEntry(EntryInfo entry) {
            try {
                return StorageProvider.Instance(entry.PortalId).RetrieveFile(entry);
            } catch (Exception) {
                return null;
            }
        }

        public static Stream GetEntryStream(int entryId, int portalId, int collectionId) {
            return GetDataForEntry(GetEntryById(entryId, portalId, collectionId));
        }

        public static EntryInfo GetEntryById(int entryId, int portalId, int collectionId) {
            return GetDmxEntriesWithAllData(portalId, 1, true, collectionId).FirstOrDefault(e => e.EntryId == entryId);
        }

        public static string GetFolderPath(EntryInfo entry) {
            var paths = new List<string>();
            while (entry != null && entry.CollectionId > 0) {
                entry = API.GetFolderByPath(entry.PortalId, entry.CollectionId, "", null, false);
                paths.Add(entry.Title);
            }
            paths.Reverse();
            return "/" + string.Join("/", paths.ToArray());
        }
    }
}
