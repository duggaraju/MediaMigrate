using Azure.ResourceManager.Media;
using Azure.ResourceManager.Media.Models;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace MediaMigrate.Ams
{
    enum TemplateType
    {
        Asset,
        Container,
        Key,
        KeyUri
    }

    internal class TemplateMapper
    {
        private readonly ILogger _logger;

        private static readonly IDictionary<TemplateType, string[]> Keys = new Dictionary<TemplateType, string[]>
        {
            [TemplateType.Container] = new[]
            {
                "ContainerName"
            },
            [TemplateType.Asset] = new[]
            {
                "AssetId",
                "AssetName",
                "AlternateId",
                "ContainerName",
                "LocatorId",
            },
            [TemplateType.Key] = new[]
            {
                "AssetId",
                "AssetName"
            },
            [TemplateType.KeyUri] = new[]
            {
                "KeyId"
            }
        };

        const string TemplateRegularExpression = @"\${(?<key>\w+)}";

        static readonly Regex _regEx =
            new(TemplateRegularExpression, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public TemplateMapper(ILogger<TemplateMapper> logger)
        {
            _logger = logger;
        }

        public static (bool, string?) Validate(string template, TemplateType type = TemplateType.Asset, bool needKey = false)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return (false, "Invalid template string");
            }

            if ((type == TemplateType.Asset || type == TemplateType.Container) && template[0] == '/')
            {
                return (false, "Asset template must start with container or bucket name which can't be empty.");
            }

            var matches = _regEx.Matches(template);
            if (needKey && matches.Count == 0)
            {
                return (false, "A template key value must be used in the template but none was used");
            }

            foreach (Match match in matches)
            {
                var group = match.Groups["key"];
                if (group == null)
                {
                    return (false, string.Empty);
                }

                var key = group.Value;
                if (!Keys[type].Contains(key))
                {
                    return (false, key);
                }
            }
            return (true, null);
        }

        public string ExpandTemplate(string template, Func<string, string?> valueExtractor)
        {
            var expandedValue = template;
            var matches = _regEx.Matches(template);
            foreach (var match in matches.Reverse())
            {
                var key = match.Groups["key"].Value;
                var value = valueExtractor(key);
                if (value != null)
                {

                    expandedValue = expandedValue.Replace(match.Value, value);
                }
            }
            _logger.LogTrace("Template {template} expaned to {value}", template, expandedValue);
            return expandedValue;
        }

        /// <summary>
        /// Expand the template to a container/bucket name and path.
        /// </summary>
        /// <returns>A tuple of container name and path prefix</returns>
        public (string Container, string Prefix) ExpandPathTemplate(string template, Func<string, string?> extractor)
        {
            string containerName;
            var path = ExpandTemplate(template, extractor);
            var index = path.IndexOf('/');
            if (index == -1)
            {
                containerName = path.ToLowerInvariant();
                path = string.Empty;
            }
            else
            {
                containerName = path.Substring(0, index).ToLowerInvariant();
                path = path.Substring(index + 1);
                if (!path.EndsWith('/'))
                {
                    path += '/';
                }
            }

            const int MaxContainerName = 63; //Max name for a Azure storage container or an S3 bucket.
            if (containerName.Length > MaxContainerName)
            {
                containerName.Substring(0, MaxContainerName);
            }
            return (containerName, path);
        }

        public (string Container, string Prefix) ExpandAssetTemplate(MediaAssetResource asset, string template)
        {
            return ExpandPathTemplate(template, key =>
            key switch
            {
                "AssetId" => (asset.Data.AssetId ?? Guid.Empty).ToString(),
                "AssetName" => asset.Data.Name,
                "ContainerName" => asset.Data.Container,
                "AlternateId" => asset.Data.AlternateId,
                "LocatorId" => GetLocatorIdAsync(asset).Result,
                _ => null
            });
        }

        public (string Container, string Prefix) ExpandPathTemplate(BlobContainerClient container, string template)
        {
            return ExpandPathTemplate(template, key => key switch
            {
                "ContainerName" => container.Name,
                _ => null,
            });
        }

        public string ExpandKeyTemplate(StreamingLocatorContentKey contentKey, string? template)
        {
            if (template == null)
            {
                return contentKey.Id.ToString();
            }
            return ExpandTemplate(template, key => key switch
            {
                "KeyId" => contentKey.Id.ToString(),
                "PolicyName" => contentKey.PolicyName,
                _ => null
            });
        }

        public string ExpandKeyTemplate(MediaAssetResource asset, string? template)
        {
            if (template == null)
            {
                return asset.Data.Name;
            }
            return ExpandTemplate(template, key => key switch
            {
                "AssetId" => asset.Data.AssetId.ToString(),
                "AssetName" => asset.Data.Name,
                _ => null
            });
        }

        public string ExpandKeyUriTemplate(string keyUri, string keyId)
        {
            return ExpandTemplate(keyUri, key => key switch
            {
                "KeyId" => keyId,
                _ => null
            });
        }

        private async Task<string> GetLocatorIdAsync(MediaAssetResource asset)
        {
            var locators = asset.GetStreamingLocatorsAsync();
            await foreach (var locator in locators)
            {
                return (locator.StreamingLocatorId ?? Guid.Empty).ToString();
            }

            _logger.LogError("No locator found for asset {name}. locator id was used in template", asset.Data.Name);
            throw new InvalidOperationException($"No locator found for asset {asset.Data.Name}");
        }
    }
}
