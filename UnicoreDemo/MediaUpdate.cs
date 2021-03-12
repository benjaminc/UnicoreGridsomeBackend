using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.PropertyEditors.ValueConverters;
using Umbraco.Core.Services;
using Umbraco.Core.Services.Implement;

public class MediaUpdate : IHostedService
{
    private readonly ILogger<FrontEndBuild> _logger;
    private readonly string _webRootPath;
    private readonly string _storageConnectionString;
    private readonly string _storageContainerName;

    public MediaUpdate(ILogger<FrontEndBuild> logger, IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
    {
        _logger = logger;
        _webRootPath = webHostEnvironment.WebRootPath;

        _storageConnectionString = configuration.GetValue<string>("Media:AzureStorage:ConnectionString");
        _storageContainerName = configuration.GetValue<string>("Media:AzureStorage:ContainerName");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_storageConnectionString))
        {
            _logger.LogWarning("No media storage connection defined");
            return Task.CompletedTask;
        }

        MediaService.Saved += MediaService_Saved;
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async void MediaService_Saved(IMediaService sender, SaveEventArgs<IMedia> e)
    {
        if (e?.SavedEntities == null || string.IsNullOrWhiteSpace(_storageConnectionString) || string.IsNullOrWhiteSpace(_storageContainerName)) return;

        try
        {
            var client = new BlobServiceClient(_storageConnectionString);
            var container = client.GetBlobContainerClient(_storageContainerName);
            var fileTypes = new FileExtensionContentTypeProvider();

            foreach (var media in e.SavedEntities)
            {
                var umbracoFile = JsonConvert.DeserializeObject<ImageCropperValue>(media.GetValue<string>(Constants.Conventions.Media.File));
                var path = Path.GetFullPath(Path.Combine(_webRootPath, umbracoFile.Src.TrimStart('/')));
                var name = Path.GetFileName(path);
                var blobName = Path.Combine(Path.GetFileName(Path.GetDirectoryName(path)), name);

                _logger.LogInformation($"Uploading media item from {umbracoFile.Src} to blob storage at {blobName}");
                var blob = container.GetBlobClient(blobName);
                await blob.DeleteIfExistsAsync();
                await blob.UploadAsync(path);
                await blob.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = fileTypes.TryGetContentType(name, out var type) ? type : "image/jpeg" });
            }
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Could not upload the blob");
        }
    }
}