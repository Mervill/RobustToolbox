using System.Runtime.CompilerServices;

namespace Robust.Tracy;

/// <summary>
/// Holds data set by the <c>Caller</c> attributes.
/// </summary>
/// <param name="MemberName">Value of <seealso cref="CallerMemberNameAttribute"/></param>
/// <param name="FilePath">Value of <seealso cref="CallerFilePathAttribute"/></param>
/// <param name="LineNumber">Value of <seealso cref="CallerLineNumberAttribute"/></param>
public readonly struct CallerInfo(uint LineNumber = 0, string? FilePath = null, string? MemberName = null)
{
    /// <summary>
    /// Value of <seealso cref="CallerLineNumberAttribute"/>
    /// </summary>
    public readonly uint LineNumber = LineNumber;

    /// <summary>
    /// Value of <seealso cref="CallerFilePathAttribute"/>
    /// </summary>
    public readonly string? FilePath = FilePath;

    /// <summary>
    /// Value of <seealso cref="CallerMemberNameAttribute"/>
    /// </summary>
    public readonly string? MemberName = MemberName;
}
