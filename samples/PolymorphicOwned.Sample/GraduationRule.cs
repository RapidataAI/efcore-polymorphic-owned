namespace PolymorphicOwned.Sample;

/// <summary>
/// The rule that decides when an annotator graduates out of an audience. It is a value object with
/// no identity of its own — the polymorphic owned shape EF Core cannot map natively. The base is an
/// <b>abstract class</b> here (it may equally be an interface) to show that path is supported.
/// </summary>
public abstract class GraduationRule
{
}

/// <summary>Graduate once a reliability score clears a threshold; demote below a lower one.</summary>
public sealed class ScoreThresholdRule : GraduationRule
{
    public double GraduationScore { get; set; }

    public double DemotionScore { get; set; }

    public int MinResponsesToGraduate { get; set; }
}

/// <summary>Graduate on measured task accuracy, gated by a task-count window.</summary>
public sealed class TaskAccuracyRule : GraduationRule
{
    public double TargetAccuracy { get; set; }

    public int MinTasks { get; set; }

    public int MaxTasks { get; set; }
}
