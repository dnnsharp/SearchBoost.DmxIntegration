using Bring2mind.DNN.Modules.DMX.Entities.Entries;
using Bring2mind.DNN.Modules.DMX.Security.Permissions;
using DnnSharp.Common;
using DnnSharp.Common2.IoC;
using DnnSharp.Common2.Services.Config;
using DnnSharp.SearchBoost.Core;
using DnnSharp.SearchBoost.Core.Behaviors;
using DnnSharp.SearchBoost.Core.Config;
using DnnSharp.SearchBoost.Core.ContentParser;
using DnnSharp.SearchBoost.Core.ContentSource;
using DnnSharp.SearchBoost.Core.Indexing;
using DnnSharp.SearchBoost.Core.Search;
using DnnSharp.SearchBoost.Core.Services;
using DnnSharp.SearchBoost.DmxIntegration.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace DnnSharp.SearchBoost.DmxIntegration.ContentSources {

    /// <summary>
    /// Stores directly to index because DMX returns content and it's too big for Indexing Queue
    /// </summary>
    public class DmxSource : IContentSource {

        IIndexingLoggerService logger { get; set; }

        IConfigService<App, ContentParserDefinition, IContentParser> contentParsersConfigService { get; set; }

        public DmxSource(IIndexingLoggerService loggerService, IConfigService<App, ContentParserDefinition, IContentParser> contentParsersConfigService) {
            this.logger = loggerService;
            this.contentParsersConfigService = contentParsersConfigService;
        }

        public IEnumerable<IndexingJob> Query(SearchBehavior behavior, DateTimeOffset? since, CancellationToken cancellationToken) {
            logger.Info(behavior.Id, () => $"DmxSource - query. Since {since}.");

            if (ReflectionUtil.IsAssemblyPresent("Bring2mind.DNN.Modules.DMX.Core") == false) {
                logger.Info(behavior.Id, () => "DmxSource - Document Exchange (DMX) is not installed");
                yield break;
            }

            IEnumerable<List<string>> mimeTypesToIndex = contentParsersConfigService.GetDefinitions().Values.Where(x =>
                x.IsSupported).Select(x => x.MimeTypes);

            logger.Info(behavior.Id, () =>
                $"DmxSource - mimeTypesToIndex: {string.Join(", ", mimeTypesToIndex.Select(x => string.Join(", ", x)))}");

            var dmxEntries = GetDmxEntries(behavior);

            if (since.HasValue) {
                dmxEntries = dmxEntries.Where(entry => entry.LastModified > since.Value.DateTime);
                logger.Debug(behavior.Id, () =>
                    $"DmxSource - Found {dmxEntries.Count()} files newer than {since.Value}");
            }

            // find the distinct, indexable entries only
            var indexableEntries = dmxEntries.Distinct(new EntryComparer());

            logger.Debug(behavior.Id, () =>
                $"DmxSource - Count of indexable distinct dmx items: {indexableEntries.Count()}.");

            List<string> mimeTypesFoundAndNotIndexed = new List<string>();
            List<string> fileNamesNotIndexed = new List<string>();
            foreach (EntryInfo entry in indexableEntries) {

                var job = new IndexingJob();

                try {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    if (mimeTypesToIndex.Any(x => x.Contains(entry.Extension.MimeType)) == false) {
                        fileNamesNotIndexed.Add(entry.OriginalFileName);

                        // save a list of not indexed mimetypes
                        if (!mimeTypesFoundAndNotIndexed.Contains(entry.Extension.MimeType))
                            mimeTypesFoundAndNotIndexed.Add(entry.Extension.MimeType);

                        continue;
                    }

                    job.ItemId = GetIndexingJobId(entry);
                    job.BehaviorId = behavior.Id;
                    job.PortalId = entry.PortalId;
                    job.TabId = new Bring2mind.DNN.Modules.DMX.Common.Settings.PortalSettings(entry.PortalId).DefaultDMXTabId;
                    job.ModuleId = -1;

                    job.ContentSourceId = "DMX";
                    job.ContentClient = "DmxFile";
                    job.ContentType = entry.Extension.MimeType;

                    job.Action = "add";
                    job.Priority = ePriorityIndexingJob.Medium;
                    job.Due = new DateTimeOffset(entry.LastModified);

                    job.Metadata.Type = "dmx";
                    job.Metadata.SubType = CleanInvalidXmlChars(ComputeFileExtension(entry));
                    job.Metadata.Url = "";
                    job.Metadata.QueryString = string.Format("Command=Core_Download&EntryId={0}", entry.EntryId);

                    job.Metadata.IgnoreSecurity = behavior.Settings.IgnoreDNNSecurity.Value;
                    if (job.Metadata.IgnoreSecurity.Value) {
                        job.Metadata.Roles = new List<string>() { "All Users" };
                    } else {
                        if (job.Metadata.Roles == null)
                            job.Metadata.Roles = new List<string>();
                        if (job.Metadata.IgnoreSecurity.Value) {
                            job.Metadata.Roles.Add("All Users");
                        } else {
                            foreach (var perm in entry.Permissions) {
                                if (perm is EntryPermissionInfo) {
                                    if ((perm as EntryPermissionInfo).AllowAccess)
                                        job.Metadata.Roles.Add((perm as EntryPermissionInfo).RoleName);
                                }
                            }
                        }
                    }

                    job.Metadata.Title = CleanInvalidXmlChars(entry.Title);
                    job.Metadata.Description = CleanInvalidXmlChars(entry.Remarks);
                    job.Metadata.Keywords = CleanInvalidXmlChars(entry.Keywords);

                    job.Metadata.AuthorId = entry.Owner;
                    job.Metadata.AuthorName = CleanInvalidXmlChars(entry.Author);

                    // in some dmx versions, LastModified is DTOffset
                    job.Metadata.DatePublished = job.DatePublished = entry.LastModified.ToUniversalTime();

                    job.Metadata.PortalId = entry.PortalId;
                    job.Metadata.ItemId = job.ItemId;
                    job.Metadata.ItemPath = entry.Path;
                    job.Metadata.OriginalName = CleanInvalidXmlChars(entry.OriginalFileName);
                    job.Metadata.OriginalId = entry.EntryId.ToString();

                    job.Metadata.ContainerId = entry.CollectionId.ToString();
                    job.Metadata.ContainerPath = DmxUtils.GetFolderPath(entry);
                    job.Metadata.ContainerName = Path.GetFileName(job.Metadata.ContainerPath);

                } catch (Exception ex) {
                    fileNamesNotIndexed.Add(entry.OriginalFileName);
                    logger.Debug(behavior.Id, () =>
                        $"Not indexed file: {entry.OriginalFileName} (DMX entryId {entry.EntryId}).", ex);
                    continue;
                }

                yield return job;
            }

            if (mimeTypesFoundAndNotIndexed.Count > 0)
                logger.Error(behavior.Id, () =>
                    $"SearchBoost did not index the following mimeTypes: {string.Join(", ", mimeTypesFoundAndNotIndexed)}");

            if (fileNamesNotIndexed.Count > 0)
                logger.Error(behavior.Id, () =>
                    $"SearchBoost did not index the following DMX files: {string.Join(", ", fileNamesNotIndexed)}");
        }

        IEnumerable<EntryInfo> GetDmxEntries(SearchBehavior behavior) {

            foreach (var folder in behavior.SourceFolders) {
                logger.Debug(behavior.Id, () => $"DmxSource - BehaviorFolder.RelativePath: {folder.RelativePath}");
                if (folder.RelativePath == "/" && folder.IncludeSubFolders) {
                    logger.Info(behavior.Id, () => "DmxSource - BehaviorFolder is Portal root including subfolders");
                    foreach (var entry in DmxUtils.GetDmxFilesForAdmin(folder.PortalId, 0, true))
                        yield return entry;
                } else if (folder.IsDmxFolder()) {
                    logger.Debug(behavior.Id, () => $"DmxSource - BehaviorFolder.RelativePath is dmx: {folder.RelativePath}");
                    foreach (var entry in DmxUtils.GetDmxFilesForAdmin(folder.PortalId, folder.GetDmxId(), folder.IncludeSubFolders))
                        yield return entry;
                }
            }
        }

        string ComputeFileExtension(EntryInfo entry) {
            if (entry.OriginalFileName == null)
                return string.Empty;

            var fileExt = Path.GetExtension(entry.OriginalFileName);
            return fileExt.Trim('.');
        }

        string GetIndexingJobId(EntryInfo entry) {
            return "dmx_" + entry.EntryId.ToString();
        }


        string CleanInvalidXmlChars(string StrInput) {
            //Returns same value if the value is empty.
            if (string.IsNullOrWhiteSpace(StrInput)) {
                return StrInput;
            }
            // From xml spec valid chars:
            // #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]    
            // any Unicode character, excluding the surrogate blocks, FFFE, and FFFF.
            string RegularExp = @"[^\x09\x0A\x0D\x20-\xD7FF\xE000-\xFFFD\x10000-x10FFFF]";
            return Regex.Replace(StrInput, RegularExp, String.Empty);
        }

        public string ComputeResultUrl(SbSearchResult searchResult, SearchContext searchContext) {
            return new GenericUrlResolver() {
                SearchResult = searchResult,
                SearchContext = searchContext
            }.GetUrlForSearchResult();
        }
    }
}
