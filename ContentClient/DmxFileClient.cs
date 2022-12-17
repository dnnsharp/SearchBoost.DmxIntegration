using DnnSharp.SearchBoost.Core.Behaviors;
using DnnSharp.SearchBoost.Core.Indexing;
using System;
using System.Collections.Generic;
using System.IO;
using DnnSharp.SearchBoost.Core.ContentClient;
using DnnSharp.SearchBoost.DmxIntegration.Utils;
using DnnSharp.SearchBoost.Core.Services;

namespace DnnSharp.SearchBoost.DmxIntegration.ContentClients {
    public class DmxFileClient : IContentClient {

        IIndexingLoggerService loggerService;

        public DmxFileClient(IIndexingLoggerService loggerService) {
            this.loggerService = loggerService;
        }

        public Stream Get(SearchBehavior behavior, string resourceId, IDictionary<string, object> options, Metadata metadata) {
            loggerService.Debug(behavior.Id, () => $"DmxFileClient - Getting stream for entry id {resourceId}");

            int entryId = ParseFileId(resourceId);
            var entry = DmxUtils.GetEntryById(entryId, metadata.PortalId, int.Parse(metadata.ContainerId));
            if (entry == null) {
                loggerService.Error(behavior.Id, () =>
                     $"DmxFileClient - Could not find entry id {entryId}");
                return null;
            }
            var stream = DmxUtils.GetDataForEntry(entry);

            if (stream != null) {
                return stream;
            } else {
                loggerService.Error(behavior.Id, () =>
                    $"DmxFileClient - Could not read content for dmx entry. DMX entry: id={entry.EntryId}, OriginalFileName={entry.OriginalFileName}");
                return null;
            }
        }

        int ParseFileId(string resourceId) {
            if (resourceId.IndexOf("dmx_") == 0)
                resourceId = resourceId.Substring("dmx_".Length);
            int entryId = 0;
            if (int.TryParse(resourceId, out entryId))
                return entryId;
            return -1;
        }
    }
}
