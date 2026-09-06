#if GLOADER
namespace AuthenticRaces.Core
{
    /// <summary>
    /// Version-independent in-memory payload. Binary schema versions translate to/from this shape.
    /// </summary>
    internal struct RaceSaveData
    {
        public string RaceName;
        public RaceAppearanceData Appearance;

        public RaceSaveData(string raceName, RaceAppearanceData appearance)
        {
            RaceName = raceName;
            Appearance = appearance;
        }
    }
}
#endif
