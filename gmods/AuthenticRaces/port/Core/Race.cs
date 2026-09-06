#if GLOADER
using Terraria;

namespace AuthenticRaces.Core
{
    /// <summary>
    /// Loader-independent race contract. This is the seam replacing MrPlagueRaces' ModType-based Race class.
    /// Add hooks here only when the direct Terraria/Harmony equivalent has been mapped and tested.
    /// </summary>
    internal abstract class Race
    {
        public int Id { get; internal set; } = -1;

        public abstract string Name { get; }
        public virtual string UpstreamFullName => "MrPlagueRaces/" + Name;
        public virtual string DisplayName => Name;
        public virtual string Description => string.Empty;

        public virtual void PreRaceChange(Player player) { }
        public virtual void PostRaceChange(Player player) { }
        public virtual void ResetEffects(Player player) { }
        public virtual void PostUpdate(Player player) { }
    }
}
#endif
