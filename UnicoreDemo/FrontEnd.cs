using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Core.Services.Implement;

public class FrontEndBuild : IHostedService
{
    private readonly ILogger<FrontEndBuild> _logger;
    private readonly Uri _webhookUrl;
    private readonly Dictionary<string, string> _headers;
    private readonly string _body;

    public FrontEndBuild(ILogger<FrontEndBuild> logger, IConfiguration configuration)
    {
        _logger = logger;

        var uriConfig = configuration.GetValue<string>("Gridsome:BuildTrigger:Uri");
        _webhookUrl = Uri.TryCreate(uriConfig, UriKind.Absolute, out var u) ? u : null;

        var headerConfig = configuration.GetValue<string>("Gridsome:BuildTrigger:Headers");
        _headers = !string.IsNullOrWhiteSpace(headerConfig) && headerConfig[0] == '{'
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(headerConfig)
            : new Dictionary<string, string>();

        _body = configuration.GetValue<string>("Gridsome:BuildTrigger:Body");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_webhookUrl == null)
        {
            _logger.LogWarning("No build trigger URL defined");
            return Task.CompletedTask;
        }

        ContentService.Deleted += TriggerBuild;
        ContentService.Moved += TriggerBuild;
        ContentService.Published += TriggerBuild;
        ContentService.Trashed += TriggerBuild;
        ContentService.Unpublished += TriggerBuild;

        _logger.LogInformation("Initializing Front-end build trigger with:\r\n    URI: {uri}\r\n    Headers: {headers}\r\n    Body: {body}", _webhookUrl, _headers, _body);

        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async void TriggerBuild(object sender, EventArgs e)
    {
        if (_webhookUrl == null) return;

        try
        {
            using (var client = new HttpClient())
            using (var message = new HttpRequestMessage(HttpMethod.Post, _webhookUrl))
            using (var content = string.IsNullOrWhiteSpace(_body) ? null : new StringContent(_body))
            {
                foreach (var header in _headers)
                {
                    switch (header.Key.ToLowerInvariant())
                    {
                        case "authorization":
                            var idx = header.Value.IndexOf(' ');
                            var scheme = idx > 0 ? header.Value.Substring(0, idx) : "BASIC";
                            var parameter = header.Value.Substring(idx + 1);
                            message.Headers.Authorization = new AuthenticationHeaderValue(scheme, parameter);
                            break;
                        case "content-type":
                            content.Headers.ContentType = new MediaTypeHeaderValue(header.Value);
                            break;
                        default:
                            message.Headers.Add(header.Key, header.Value);
                            break;
                    }
                }

                if (content != null) message.Content = content;

                var response = await client.SendAsync(message);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully triggered a front-end build request with {code} result: {body}", response.StatusCode, body);
                }
                else
                {
                    _logger.LogError("Could not successfully trigger a front-end build request. It return {code} with a body of {response}", response.StatusCode, body);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not call the front-end build trigger");
        }
    }
}