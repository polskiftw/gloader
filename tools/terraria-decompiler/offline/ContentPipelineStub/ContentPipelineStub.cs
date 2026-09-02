using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace Microsoft.Xna.Framework.Content.Pipeline
{
    public enum TargetPlatform
    {
        Windows = 0,
        Xbox360 = 1,
        WindowsPhone = 2
    }

    public class ContentIdentity
    {
    }

    public class OpaqueDataDictionary : Dictionary<string, object>
    {
    }

    public class ExternalReference<T>
    {
    }

    public abstract class ContentBuildLogger
    {
        public abstract void LogImportantMessage(string message, params object[] messageArgs);
        public abstract void LogMessage(string message, params object[] messageArgs);
        public abstract void LogWarning(string helpLink, ContentIdentity contentIdentity, string message, params object[] messageArgs);
    }

    public abstract class ContentProcessorContext
    {
        public abstract TargetPlatform TargetPlatform { get; }
        public abstract GraphicsProfile TargetProfile { get; }
        public abstract ContentBuildLogger Logger { get; }
        public abstract OpaqueDataDictionary Parameters { get; }
        public abstract string BuildConfiguration { get; }
        public abstract string OutputFilename { get; }
        public abstract string OutputDirectory { get; }
        public abstract string IntermediateDirectory { get; }

        public abstract void AddDependency(string filename);
        public abstract void AddOutputFile(string filename);
        public abstract TOutput BuildAndLoadAsset<TInput, TOutput>(ExternalReference<TInput> sourceAsset, string processorName, OpaqueDataDictionary processorParameters, string importerName);
        public abstract ExternalReference<TOutput> BuildAsset<TInput, TOutput>(ExternalReference<TInput> sourceAsset, string processorName, OpaqueDataDictionary processorParameters, string importerName, string assetName);
        public abstract TOutput Convert<TInput, TOutput>(TInput input, string processorName, OpaqueDataDictionary processorParameters);
    }

    public abstract class ContentProcessor<TInput, TOutput>
    {
        public abstract TOutput Process(TInput input, ContentProcessorContext context);
    }
}

namespace Microsoft.Xna.Framework.Content.Pipeline.Graphics
{
    public class ContentItem
    {
    }

    public class EffectContent : ContentItem
    {
        public string EffectCode { get; set; }
    }

    public class CompiledEffectContent : ContentItem
    {
        public byte[] GetEffectCode()
        {
            throw new NotSupportedException("Metadata-only compatibility shim.");
        }
    }
}

namespace Microsoft.Xna.Framework.Content.Pipeline.Processors
{
    using Microsoft.Xna.Framework.Content.Pipeline.Graphics;

    public class EffectProcessor : ContentProcessor<EffectContent, CompiledEffectContent>
    {
        public override CompiledEffectContent Process(EffectContent input, ContentProcessorContext context)
        {
            throw new NotSupportedException("Metadata-only compatibility shim.");
        }
    }
}
