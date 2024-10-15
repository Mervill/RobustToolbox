using System;
using System.Collections.Generic;
using System.Text;

namespace TracyProfiler;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public class TracyAutowireZoneOptionsAttribute : Attribute
{
    public uint Color;

    public TracyAutowireZoneOptionsAttribute(uint color = 0)
    {
        Color = color;
    }
}
