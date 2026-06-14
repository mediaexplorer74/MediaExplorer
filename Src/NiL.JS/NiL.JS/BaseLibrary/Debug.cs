using System;
using NiL.JS.Core;

namespace NiL.JS.BaseLibrary;

public static class Debug
{
    public static void writeln(Arguments args)
    {
        for (var i = 0; i < args._iValue; i++)
        {
            if (i < args._iValue)
                System.Diagnostics.Debug.Write(args[0]);
            else
                System.Diagnostics.Debug.WriteLine(args[args._iValue - 1]);
        }
    }

    public static void write(Arguments args)
    {
        for (var i = 0; i < args._iValue; i++)
            System.Diagnostics.Debug.Write(args[0]);
    }

    public static void assert(Arguments args)
    {
        if (!(bool)args[0])
            throw new Exception(args[0].ToString());
    }

    public static JSValue asserta(Function f, JSValue sample)
    {
        if (sample == null || !sample.Exists)
            sample = Boolean.True;

        if (!JSObject.@is(f.Call(null), sample))
        {
            var message = f.ToString();
            message = message.Substring(message.IndexOf("=>") + 2).Trim();
            throw new Exception(message + " not equals " + sample);
        }

        return JSValue.undefined;
    }
}
