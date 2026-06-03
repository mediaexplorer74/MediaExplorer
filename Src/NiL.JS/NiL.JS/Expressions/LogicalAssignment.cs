using System;
using System.Collections.Generic;
using NiL.JS.Core;
using NiL.JS.Core.Functions;

namespace NiL.JS.Expressions;

#if !(PORTABLE || NETCORE)
[Serializable]
#endif
public sealed class LogicalAssignment : Expression
{
    private readonly OperationType _logicalOp;

    protected internal override bool ContextIndependent => false;
    protected internal override bool LValueModifier => true;

    internal LogicalAssignment(Expression left, Expression right, OperationType logicalOp)
        : base(left, right, false)
    {
        _logicalOp = logicalOp;
    }

    public override JSValue Evaluate(Context context)
    {
        var field = _left.EvaluateForWrite(context);
        var source = context._objectSource;
        var leftValue = field._valueType == JSValueType.Property
            ? CallGetter(field._oValue as PropertyPair, source)
            : field;

        bool shouldAssign;
        switch (_logicalOp)
        {
            case OperationType.LogicalOr:
                shouldAssign = !(bool)leftValue;
                break;
            case OperationType.LogicalAnd:
                shouldAssign = (bool)leftValue;
                break;
            case OperationType.NullishCoalescing:
                shouldAssign = leftValue.ValueType == JSValueType.Undefined || leftValue.ValueType == JSValueType.NotExists || leftValue.ValueType == JSValueType.NotExistsInObject;
                break;
            default:
                shouldAssign = false;
                break;
        }

        if (shouldAssign)
        {
            var temp = _right.Evaluate(context);
            if (field._valueType == JSValueType.Property)
            {
                var setterArgs = new Arguments();
                setterArgs.Add(temp);
                var propPair = field._oValue as PropertyPair;
                if (propPair.Setter != null)
                    CallSetter(propPair, source, setterArgs);
            }
            else if ((field._attributes & JSValueAttributesInternal.ReadOnly) == 0)
            {
                field.Assign(temp);
            }
            return temp;
        }

        return leftValue;
    }

    public override bool Build(ref CodeNode _this, int expressionDepth, int scopeLevel, Dictionary<string, VariableDescriptor> variables, CodeContext codeContext, InternalCompilerMessageCallback message, FunctionInfo stats, Options opts)
    {
        base.Build(ref _this, expressionDepth, scopeLevel, variables, codeContext, message, stats, opts);
        if ((codeContext & CodeContext.InExpression) != 0)
        {
            // Need to save result when used in expression context
        }
        return false;
    }

    public override void Optimize(ref CodeNode _this, FunctionDefinition owner, InternalCompilerMessageCallback message, Options opts, FunctionInfo stats)
    {
    }

    public override T Visit<T>(Visitor<T> visitor)
    {
        return visitor.Visit(this);
    }

    public override string ToString()
    {
        string op = _logicalOp switch
        {
            OperationType.LogicalOr => "||=",
            OperationType.LogicalAnd => "&&=",
            OperationType.NullishCoalescing => "??=",
            _ => "?="
        };
        return "(" + _left + " " + op + " " + _right + ")";
    }

    private static JSValue CallGetter(PropertyPair pp, JSValue source)
    {
        return ((ICallable)pp.Getter).Call(source, new Arguments());
    }

    private static void CallSetter(PropertyPair pp, JSValue source, Arguments args)
    {
        ((ICallable)pp.Setter).Call(source, args);
    }
}
