using System;

namespace TracyProfiler
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class TracyAutowireIgnoreMethodAttribute : Attribute
    {
    }
}
