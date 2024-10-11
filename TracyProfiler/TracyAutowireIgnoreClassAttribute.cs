using System;

namespace TracyProfiler
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public class TracyAutowireIgnoreClassAttribute : Attribute
    {
    }
}
