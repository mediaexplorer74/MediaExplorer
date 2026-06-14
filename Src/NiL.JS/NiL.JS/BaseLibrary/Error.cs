//#define CALLSTACKTOSTRING

using System;
using NiL.JS.Core;
using NiL.JS.Core.Interop;

namespace NiL.JS.BaseLibrary;

[Serializable]
public class Error
{
    [DoNotEnumerate]
    public JSValue message
    {
        [Hidden]
        get;
        private set;
    }
    [DoNotEnumerate]
    public JSValue name
    {
        [Hidden]
        get;
        set;
    }
    [DoNotEnumerate]
    public Error()
    {
        name = this.GetType().Name;
        message = "";
    }

    [DoNotEnumerate]
    public Error(Arguments args)
    {
        name = this.GetType().Name;
        message = args[0].ToString();
    }

    [DoNotEnumerate]
    public Error(string message)
    {
        name = this.GetType().Name;
        this.message = message;
    }
    [Hidden]
    public override string ToString()
    {
        string mstring;
        string nstring;
        if (message == null
            || message._valueType <= JSValueType.Undefined
            || string.IsNullOrEmpty((mstring = message.ToString())))
            return name.ToString()
;
        if (name == null
            || name._valueType <= JSValueType.Undefined
            || string.IsNullOrEmpty((nstring = name.ToString())))
            return mstring
;
        return nstring + ": " + mstring
;
    }

    [DoNotEnumerate]
    [CLSCompliant(false)]
    public JSValue toString()
    {
        return ToString();
    }
}
