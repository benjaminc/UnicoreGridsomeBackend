using Algolia.Search.Clients;
using Examine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services.Implement;
using Umbraco.Examine;

namespace UnicoreDemo
{
    public class SearchUpdater : IHostedService
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly BlockingCollection<(bool Remove, IndexItem Item)> _indexItems;
        private readonly ILogger<SearchUpdater> _logger;
        private readonly IExamineManager _examineManager;
        private readonly string _applicationId;
        private readonly string _apiKey;
        private readonly string _indexName;

        public SearchUpdater(ILogger<SearchUpdater> logger, IConfiguration configuration, IExamineManager examineManager)
        {
            _logger = logger;
            _examineManager = examineManager;

            _indexItems = new BlockingCollection<(bool, IndexItem)>();
            _applicationId = configuration.GetValue<string>("Search:Algolia:ApplicationId");
            _apiKey = configuration.GetValue<string>("Search:Algolia:ApiKey");
            _indexName = configuration.GetValue<string>("Search:Algolia:IndexName");
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_applicationId))
            {
                _logger.LogInformation("No application ID defined for Algolia");
                return Task.CompletedTask;
            }

            ContentService.Published += (s, e) => QueueForSync(false, e.PublishedEntities);
            ContentService.Unpublished += (s, e) => QueueForSync(true, e.PublishedEntities);

#pragma warning disable CS4014
            // Start background thread to synchronize changes as they become available
            Task.Run(SyncChanges);
#pragma warning restore CS4014

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _indexItems.CompleteAdding();
            _cancellationTokenSource.Cancel();

            return Task.CompletedTask;
        }

        private void QueueForSync(bool removing, IEnumerable<IContent> contents)
        {
            try
            {
                contents.Where(x => string.Equals(x?.ContentType?.Alias, "simplePage", StringComparison.InvariantCultureIgnoreCase))
                    .Select(x => (removing, new IndexItem
                    {
                        ObjectID = x.Key.ToString().ToLowerInvariant(),
                        ItemType = x.ContentType.Alias,
                        UpdateDate = x.UpdateDate.ToUniversalTime().ToString("O"),
                        Title = x.GetValue<string>("title").IfNullOrWhiteSpace(x.Name),
                        Content = x.GetValue<string>("content")
                    }))
                    .ToList()
                    .ForEach(x => _indexItems.Add(x));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not queue items for sync to algolia");
            }
        }

        private async Task SyncChanges()
        {
            var token = _cancellationTokenSource.Token;
            if (token.IsCancellationRequested) return;

            try
            {
                var items = new List<(bool Removing, IndexItem Item)>();

                while (!_indexItems.IsAddingCompleted)
                {
                    if (token.IsCancellationRequested) return;
                    items.Clear();

                    while (_indexItems.TryTake(out var item, 1000, token))
                    {
                        items.Add(item);
                    }

                    if (token.IsCancellationRequested) return;
                    if (items.Count > 0)
                    {
                        try
                        {
                            var client = new SearchClient(_applicationId, _apiKey);
                            var index = client.InitIndex(_indexName);

                            var toAdd = items.Where(x => !x.Removing).Select(x => x.Item).ToList();
                            if (toAdd.Count > 0)
                            {
                                await index.SaveObjectsAsync(toAdd, ct: token);
                                _logger.LogInformation("Saved to Algolia {ids}", string.Join(", ", toAdd.Select(x => x.ObjectID)));
                            }

                            var toRemove = items.Where(x => x.Removing).Select(x => x.Item.ObjectID).ToList();
                            if (toRemove.Count > 0)
                            {
                                await index.DeleteObjectsAsync(toRemove, ct: token);
                                _logger.LogInformation("Deleted from Algolia {ids}", string.Join(", ", toRemove));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Could not sync to algolia {count} items", items.Count);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not sync item to algolia");
            }
        }

        private class IndexItem
        {
            public string ObjectID { get; set; }
            public string ItemType { get; set; }
            public string UpdateDate { get; set; }
            public string Title { get; set; }
            public string Content { get; set; }
        }
    }
}
