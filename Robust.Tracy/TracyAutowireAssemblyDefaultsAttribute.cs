using System;
using System.Collections.Generic;
using System.Text;

namespace Robust.Tracy;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public class TracyAutowireAssemblyDefaultsAttribute : Attribute
{
    public uint Color;

    public TracyAutowireAssemblyDefaultsAttribute(uint color = 0)
    {
        Color = color;
    }
}
