using System;
using NiL.JS.Core;
using NiL.JS.Core.Interop;

namespace NiL.JS.BaseLibrary;

[Prototype(typeof(Error))]
[Serializable]
public sealed class RangeError : Error
{
    [DoNotEnumerate]
    public RangeError()
    {

    }

    [DoNotEnumerate]
    public RangeError(Arguments args)
        : base(args[0].ToString())
    {

    }

    [DoNotEnumerate]
    public RangeError(string message)
        : base(message)
    {

    }
}
