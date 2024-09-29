using System;

namespace TracyProfiler
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class TracyAutowireIgnoreClassAttribute : Attribute
    {
    }
}
