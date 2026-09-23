using System.Collections.Generic;

namespace GLoader
{
    internal sealed class ModSource
    {
        public ModSource(string id, string displayName, IReadOnlyList<string> sourceFiles, string directory)
        {
            Id = id;
            DisplayName = displayName;
            SourceFiles = sourceFiles;
            Directory = directory;
        }

        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public IReadOnlyList<string> SourceFiles { get; private set; }
        public string Directory { get; private set; }
    }
}
