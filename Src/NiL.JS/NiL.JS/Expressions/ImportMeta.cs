using System.Collections.Generic;
using NiL.JS.BaseLibrary;
using NiL.JS.Core;

namespace NiL.JS.Expressions;

public sealed class ImportMeta : Expression
{
    protected internal override bool ContextIndependent
    {
        get { return false; }
    }

    protected internal override bool NeedDecompose
    {
        get { return false; }
    }

    protected internal override bool LValueModifier
    {
        get { return false; }
    }

    public ImportMeta()
    {
    }

    public override JSValue Evaluate(Context context)
    {
        var ctx = context;
        while (ctx != null)
        {
            if (ctx._module != null)
                break;
            ctx = ctx._parent;
        }

        if (ctx == null || ctx._module == null)
            ExceptionHelper.Throw(new SyntaxError("Cannot use 'import.meta' outside a module"));

        var url = ctx._module.FilePath ?? "";
        var result = new JSObject();
        result._valueType = JSValueType.Object;
        result._oValue = new Dictionary<string, JSValue>(System.StringComparer.Ordinal)
        {
            ["url"] = url
        };
        return result;
    }

    public override bool Build(ref CodeNode _this, int expressionDepth, int scopeLevel, Dictionary<string, VariableDescriptor> variables, CodeContext codeContext, InternalCompilerMessageCallback message, FunctionInfo stats, Options opts)
    {
        return false;
    }

    public override void Optimize(ref Core.CodeNode _this, FunctionDefinition owner, InternalCompilerMessageCallback message, Options opts, FunctionInfo stats)
    {
    }

    public override T Visit<T>(Visitor<T> visitor)
    {
        return visitor.Visit(this);
    }

    public override string ToString()
    {
        return "import.meta";
    }
}
