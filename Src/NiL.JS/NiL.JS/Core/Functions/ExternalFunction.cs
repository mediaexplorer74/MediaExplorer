using System;
using System.Linq;
using System.Reflection;
using NiL.JS.BaseLibrary;
using NiL.JS.Core.Interop;

namespace NiL.JS.Core.Functions;

/// <summary>
/// Представляет функцию платформы с фиксированной сигнатурой.
/// </summary>
[Prototype(typeof(Function), true)]
[Serializable]
public sealed class ExternalFunction : Function
{
    public override string name
    {
        get
        {
            return System.Reflection.RuntimeReflectionExtensions.GetMethodInfo(_delegate).Name;
        }
    }
    
    public override JSValue prototype
    {
        get
        {
            return null;
        }
        set
        {
        }
    }

    private readonly ExternalFunctionDelegate _delegate;

    public ExternalFunctionDelegate Delegate { get { return _delegate; } }

    public ExternalFunction(ExternalFunctionDelegate @delegate)
    {
        if (_length == null)
            _length = new Number(0) { _attributes = JSValueAttributesInternal.ReadOnly | JSValueAttributesInternal.DoNotDelete | JSValueAttributesInternal.DoNotEnumerate };

        var paramCountAttrbt = ((Delegate)@delegate).GetMethodInfo().GetCustomAttributes(typeof(ArgumentsCountAttribute), false).ToArray();
        _length._iValue = paramCountAttrbt.Length > 0 ? ((ArgumentsCountAttribute)paramCountAttrbt[0]).Count : 1;
        
        if (@delegate == null)
            throw new ArgumentNullException();

        _delegate = @delegate;
        RequireNewKeywordLevel = BaseLibrary.RequireNewKeywordLevel.Both;
    }

    protected internal override JSValue Invoke(bool construct, JSValue targetObject, Arguments arguments)
    {
        var res = _delegate(targetObject, arguments);
        if (res == null)
            return NotExists;
        return res;
    }
    
    public override string ToString(bool headerOnly)
    {
        var result = "function " + name + "()";

        if (!headerOnly)
        {
            result += " { [native code] }";
        }

        return result;
    }
}
