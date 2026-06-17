namespace Sed.Core.Model;

/// <summary>
/// Target game for a level (TProjectType in the original). The Sith engine
/// editor supports Dark Forces II / Jedi Knight, Mysteries of the Sith, and
/// Indiana Jones and the Infernal Machine.
/// </summary>
public enum ProjectType
{
    JediKnight,
    MysteriesOfTheSith,
    InfernalMachine,
}

/// <summary>Thing flags (TF_* constants in J_LEVEL.PAS).</summary>
[System.Flags]
public enum ThingFlags
{
    None = 0,
    Invisible = 0x10,
    NoEasy = 0x1000,
    NoMedium = 0x2000,
    NoHard = 0x4000,
    NoMulti = 0x8000,
    Disabled = 0x80000,
}
