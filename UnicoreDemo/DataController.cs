using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.Scoping;
using Umbraco.Core.Services;
using Umbraco.Web;
using Umbraco.Web.Models;
using Umbraco.Web.Routing;

[Route("/api/data")]
public class DataController : Controller
{
    private readonly ILogger<DataController> _logger;
    private readonly IScopeProvider _scopeProvider;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IFileService _fileService;
    private readonly IPublishedUrlProvider _publishedUrlProvider;

    public DataController(ILogger<DataController> logger, IScopeProvider scopeProvider, IUmbracoContextFactory umbracoContextFactory, IFileService fileService, IPublishedUrlProvider publishedUrlProvider)
    {
        _logger = logger;
        _scopeProvider = scopeProvider;
        _umbracoContextFactory = umbracoContextFactory;
        _fileService = fileService;
        _publishedUrlProvider = publishedUrlProvider;
    }

    [HttpGet, Route("content")]
    public IEnumerable<IDictionary<string, object>> Content()
    {
        _logger.LogInformation("Received GetContent call");

        var templates = _fileService.GetTemplates().ToDictionary(x => x.Id, x => x.Alias);
        using (var scope = _scopeProvider.CreateScope())
        using (var ucr = _umbracoContextFactory.EnsureUmbracoContext())
        {
            var context = ucr.UmbracoContext;

            foreach (var content in GetContent(context.Content.GetAtRoot(), templates))
            {
                yield return content;
            }
        }
    }

    private IEnumerable<IDictionary<string, object>> GetContent(IEnumerable<IPublishedContent> contents, Dictionary<int, string> templates)
    {
        if (contents == null) yield break;

        foreach (var content in contents)
        {
            yield return GetContent(content, templates);

            var children = content.Children;
            if (children != null)
            {
                foreach (var child in GetContent(children, templates))
                {
                    yield return child;
                }
            }
        }
    }

    private IDictionary<string, object> GetContent(IPublishedContent content, Dictionary<int, string> templates)
    {
        var map = new Dictionary<string, object>
        {
            ["_childKeys"] = content.Children.Select(x => x.Key.ToString()).ToArray(),
            ["_contentType"] = content.ContentType.Alias,
            ["_createDate"] = content.CreateDate.ToUniversalTime().ToString("R"),
            ["_id"] = content.Id,
            ["_level"] = content.Level,
            ["_key"] = content.Key.ToString(),
            ["_name"] = content.Name,
            ["_parentKey"] = content.Parent?.Key.ToString(),
            ["_sortOrder"] = content.SortOrder,
            ["_template"] = content.TemplateId.HasValue && templates.TryGetValue(content.TemplateId.Value, out var t) ? t : null,
            ["_updateDate"] = content.UpdateDate.ToUniversalTime().ToString("R"),
            ["_url"] = content.Url(_publishedUrlProvider, mode: UrlMode.Relative)
        };

        foreach (var prop in content.Properties)
        {
            map[prop.Alias] = GetValue(prop.GetValue());
        }

        return map;
    }

    private object GetValue(object value) => value switch
    {
        bool v => v,
        byte v => (int)v,
        sbyte v => (int)v,
        short v => (int)v,
        ushort v => (int)v,
        int v => v,
        uint v => (long)v,
        long v => v,
        ulong v => v,
        float v => (double)v,
        double v => v,
        decimal v => v,
        DateTime v => v.ToUniversalTime().ToString("R"),
        DateTimeOffset v => v.ToUniversalTime().ToString("R"),
        Link v => JsonConvert.DeserializeObject(JsonConvert.SerializeObject(v)),
        JToken v => v,
        string v => v,
        IEnumerable v => v.Cast<object>().Select(x => GetValue(v)).ToArray(),
        _ => value?.ToString(),
    };

    [HttpGet, Route("search")]
    public IEnumerable<IDictionary<string, object>> Search()
    {
        _logger.LogInformation("Received GetSearch call");

        using (var scope = _scopeProvider.CreateScope())
        using (var ucr = _umbracoContextFactory.EnsureUmbracoContext())
        {
            var context = ucr.UmbracoContext;

            foreach (var content in GetSearch(context.Content.GetAtRoot()))
            {
                yield return content;
            }
        }
    }

    private IEnumerable<IDictionary<string, object>> GetSearch(IEnumerable<IPublishedContent> contents)
    {
        if (contents == null) yield break;

        foreach (var content in contents)
        {
            yield return new Dictionary<string, object>();

            var children = content.Children;
            if (children != null)
            {
                foreach (var child in GetSearch(children))
                {
                    yield return child;
                }
            }
        }
    }
}