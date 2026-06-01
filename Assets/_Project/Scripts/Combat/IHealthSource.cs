namespace Celestia.Combat
{
    /// <summary>
    /// Read-only view onto whatever component owns a health pool. Lets UI (e.g.,
    /// WorldHealthBar) bind to either Health or PlayerHealth without caring which
    /// concrete type it's pointing at.
    /// </summary>
    public interface IHealthSource
    {
        /// <summary>Current HP normalised to [0, 1]. 0 when dead, 1 when full.</summary>
        float Fraction01 { get; }

        bool IsDead { get; }
    }
}
