using System;
using NiL.JS.Core;
using NiL.JS.Core.Interop;

namespace NiL.JS.BaseLibrary;

[Prototype(typeof(Error))]
[Serializable]
public sealed class TypeError : Error
{
    [DoNotEnumerate]
    public TypeError(Arguments args)
        : base(args[0].ToString())
    {

    }

    [DoNotEnumerate]
    public TypeError()
    {

    }

    [DoNotEnumerate]
    public TypeError(string message)
        : base(message)
    {
    }
}
