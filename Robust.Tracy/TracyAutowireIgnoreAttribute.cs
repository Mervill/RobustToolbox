using System;

namespace TracyProfiler;

/// <summary>
/// Supress generating a profiler zone for the annotated item. When used on a <see langword="class"/> or <see langword="struct"/> all functions inside that object are ignored.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public class TracyAutowireIgnoreAttribute : Attribute
{
}
