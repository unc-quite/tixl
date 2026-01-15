using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

namespace T3.Core.Resource;

public static partial class ResourceManager
{
    public const char PathSeparator = '/';

    public static bool TryResolveUri(string uri,
                                     IResourceConsumer consumer,
                                     out string absolutePath,
                                     out IResourcePackage resourceContainer,
                                     bool isFolder = false)
    {
        var packages = consumer?.AvailableResourcePackages.ToArray();
        if (string.IsNullOrWhiteSpace(uri))
        {
            absolutePath = string.Empty;
            resourceContainer = null;
            return false;
        }

        uri.ToForwardSlashesUnsafe();

        if (uri.StartsWith('/')) // todo: this will be the only way to reference resources in the future?
        {
            return HandlePackageUri(uri, packages, out absolutePath, out resourceContainer, isFolder);
        }

        LocalPathBackwardsCompatibility(uri, out var isAbsolute, out var backCompatRanges);
        if (isAbsolute)
        {
            absolutePath = uri.ToForwardSlashes();
            resourceContainer = null;
            return Exists(absolutePath, isFolder);
        }

        IReadOnlyList<string> backCompatibleUris = null;

        if (packages != null)
        {
            if (CheckUri(uri, packages, out absolutePath, out resourceContainer, isFolder))
                return true;

            backCompatibleUris = PopulateBackCompatPaths(uri, backCompatRanges);

            foreach (var backCompatibleUri in backCompatibleUris)
            {
                if (CheckUri(backCompatibleUri, packages, out absolutePath, out resourceContainer, isFolder))
                    return true;
            }
        }

        // TODO: What is that "*.hlsl" extension here? This method should be file type agnostic.
        var sharedResourcePackages = uri.EndsWith(".hlsl") ? _shaderPackages : _sharedResourcePackages;

        if (CheckUri(uri, sharedResourcePackages, out absolutePath, out resourceContainer, isFolder))
        {
            return true;
        }

        backCompatibleUris ??= PopulateBackCompatPaths(uri, backCompatRanges);

        foreach (var backCompatPath in backCompatibleUris)
        {
            if (CheckUri(backCompatPath, sharedResourcePackages, out absolutePath, out resourceContainer, isFolder))
                return true;
        }

        absolutePath = string.Empty;
        resourceContainer = null;
        return false;
    }

    private static bool Exists(string absolutePath, bool isFolder) => isFolder
                                                                          ? Directory.Exists(absolutePath)
                                                                          : File.Exists(absolutePath);

    private static bool CheckUri(string uri,
                                 IEnumerable<IResourcePackage> resourceContainers,
                                 out string absolutePath,
                                 out IResourcePackage resourceContainer,
                                 bool isFolder)
    {
        foreach (var package in resourceContainers)
        {
            var resourcesFolder = package.ResourcesFolder;
            var path = Path.Combine(resourcesFolder, uri);

            if (Exists(path, isFolder))
            {
                absolutePath = path;
                absolutePath.ToForwardSlashesUnsafe();
                resourceContainer = package;
                return true;
            }
        }

        absolutePath = string.Empty;
        resourceContainer = null;
        return false;
    }

    private static bool HandlePackageUri(string packageUri, IEnumerable<IResourcePackage> resourceContainers, out string absolutePath,
                                         out IResourcePackage resourceContainer, bool isFolder)
    {
        var packageUriWithoutSlash = packageUri.AsSpan(1);
        var packageEnd = packageUriWithoutSlash.IndexOf('/');

        if (packageEnd == -1)
        {
            absolutePath = string.Empty;
            resourceContainer = null;
            return false;
        }

        var packageName = packageUriWithoutSlash[..packageEnd];
        var uriWithoutPackage = packageUriWithoutSlash[(packageEnd + 1)..].ToString();

        if (resourceContainers != null)
        {
            foreach (var container in resourceContainers)
            {
                if (CheckPackagePath(container, packageName, uriWithoutPackage, out absolutePath, isFolder))
                {
                    resourceContainer = container;
                    return true;
                }
            }
        }

        var sharedResourcePackages = packageUriWithoutSlash.EndsWith(".hlsl")
                                         ? _shaderPackages
                                         : _sharedResourcePackages;

        foreach (var container in sharedResourcePackages)
        {
            if (CheckPackagePath(container, packageName, uriWithoutPackage, out absolutePath, isFolder))
            {
                resourceContainer = container;
                return true;
            }
        }

        absolutePath = string.Empty;
        resourceContainer = null;
        return false;

        static bool CheckPackagePath(IResourcePackage container, ReadOnlySpan<char> packageName, string localUri, out string absolutePath,
                                     bool isFolder)
        {
            var containerName = container.Name;
            if (containerName == null)
            {
                absolutePath = string.Empty;
                return false;
            }

            if (StringUtils.Equals(containerName, packageName, true))
            {
                var path = Path.Combine(container.ResourcesFolder, localUri);
                if (Exists(path, isFolder))
                {
                    absolutePath = path;
                    absolutePath.ToForwardSlashesUnsafe();
                    return true;
                }
            }

            absolutePath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// This will try to first create a localUrl, then a packageUrl, and finally fall back to an absolute path.
    /// </summary>
    public static bool TryConstructAssetUri(string absolutePath,
                                            Instance composition,
                                            [NotNullWhen(true)] out string assetUri)
    {
        assetUri = null;
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        var normalizedPath = absolutePath.Replace("\\", "/");

        var localPackage = composition.Symbol.SymbolPackage;
        
        // Disable localUris for now
        //var localRoot = localPackage.ResourcesFolder.TrimEnd('/') + "/";
        // if (normalizedPath.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase))
        // {
        //     // Dropping the root folder gives us the local relative path
        //     assetUri = normalizedPath[localRoot.Length..];
        //     return true;
        // }

        // 3. Check other packages
        foreach (var p in composition.AvailableResourcePackages)
        {
            if (p == localPackage) continue;

            var packageRoot = p.ResourcesFolder.TrimEnd('/') + "/";
            if (normalizedPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Tixl 4.0 format...
                assetUri = $"/{p.Name}/{normalizedPath[packageRoot.Length..]}";
                return true;
            }
        }

        // 4. Fallback to Absolute
        assetUri = normalizedPath;
        return true;
    }
}