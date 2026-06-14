using System;
using NiL.JS.Core;
using NiL.JS.Core.Interop;

namespace NiL.JS.BaseLibrary;

[Prototype(typeof(Error))]
[Serializable]
public sealed class ReferenceError : Error
{
    [DoNotEnumerate]
    public ReferenceError(Arguments args)
        : base(args[0].ToString())
    {
    }

    [DoNotEnumerate]
    public ReferenceError()
    {
    }

    [DoNotEnumerate]
    public ReferenceError(string message)
        : base(message)
    {
    }
}
