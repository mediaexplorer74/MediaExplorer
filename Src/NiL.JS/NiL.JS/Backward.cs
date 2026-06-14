using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace System.Runtime.CompilerServices
{
    [CLSCompliant(false)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        private readonly string[] _transformNames;

        public TupleElementNamesAttribute(string[] transformNames)
        {

            _transformNames = transformNames;
        }

        public IList<string> TransformNames => _transformNames;
    }
}

namespace System
{
    public struct ValueTuple<T1, T2>
    {
        public readonly T1 Item1;
        public readonly T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }
        
    public struct ValueTuple<T1, T2, T3>
    {
        public readonly T1 Item1;
        public readonly T2 Item2;
        public readonly T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
    }
}

namespace NiL.JS.Backward
{
    internal static class EmpryArrayHelper
    {
        private static class EmptyArrayContainer<T>
        {
            public static readonly T[] EmptyArray = new T[0];
        }

        public static T[] Empty<T>()
        {
            return EmptyArrayContainer<T>.EmptyArray;
        }
    }

    internal static class Backward
    {
        internal static ReadOnlyCollection<T> AsReadOnly<T>(this IList<T> self)
        {
            return new ReadOnlyCollection<T>(self);
        }

        internal static ReadOnlyCollection<T> AsReadOnly<T>(this List<T> self)
        {
            return new ReadOnlyCollection<T>(self);
        }

        internal static bool IsSubclassOf(this Type self, Type sourceType)
        {
            return self != sourceType && self.GetTypeInfo().IsAssignableFrom(sourceType.GetTypeInfo());
        }

        internal static object[] GetCustomAttributes(this Type self, Type attributeType, bool inherit)
        {
            return self.GetTypeInfo().GetCustomAttributes(attributeType, inherit).ToArray();
        }

        internal static bool IsDefined(this Type self, Type attributeType, bool inherit)
        {
            return self.GetTypeInfo().IsDefined(attributeType, inherit);
        }

        internal static MemberTypes GetMemberType(this MemberInfo self)
        {
            if (self is ConstructorInfo)
                return MemberTypes.Constructor;
            if (self is EventInfo)
                return MemberTypes.Event;
            if (self is FieldInfo)
                return MemberTypes.Field;
            if (self is MethodInfo)
                return MemberTypes.Method;
            if (self is TypeInfo)
                return MemberTypes.TypeInfo;
            if (self is PropertyInfo)
                return MemberTypes.Property;
            return MemberTypes.Custom; // чёт своё, пускай сами разбираются
        }

        private static readonly Type[] _Types =
            {
                null,
                typeof(object),
                Type.GetType("System.DBNull"),
                typeof(bool),
                typeof(char),
                typeof(sbyte),
                typeof(byte),
                typeof(short),
                typeof(ushort),
                typeof(int),
                typeof(uint),
                typeof(long),
                typeof(ulong),
                typeof(float),
                typeof(double),
                typeof(decimal),
                typeof(DateTime),
                null,
                typeof(string)
            };


    }
}

namespace System.Reflection.Emit
{
    public static class TypeBuilderPolyfill
    {
        public static Type CreateType(this TypeBuilder builder) => builder.CreateTypeInfo().AsType();
    }
}


namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    public sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue) { ReturnValue = returnValue; }
        public bool ReturnValue { get; }
    }
}

namespace NiL.JS.Backward
{
    public static class KeyValuePairExtensions
    {
        public static void Deconstruct<T1, T2>(this KeyValuePair<T1, T2> keyValuePair, out T1 value1, out T2 value2)
        {
            value1 = keyValuePair.Key;
            value2 = keyValuePair.Value;
        }
    }
}


namespace System
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false)]
    public sealed class SerializableAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class NonSerializedAttribute : Attribute { }

    public interface ICloneable
    {
        object Clone();
    }

    public class AssemblyLoadEventArgs : EventArgs
    {
        public AssemblyLoadEventArgs(Assembly loadedAssembly) { LoadedAssembly = loadedAssembly; }
        public Assembly LoadedAssembly { get; }
    }

    public class ApplicationException : Exception
    {
        public ApplicationException() { }
        public ApplicationException(string message) : base(message) { }
        public ApplicationException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class DBNull
    {
        private DBNull() { }
        public static readonly DBNull Value = new DBNull();
    }

    public class RuntimeWrappedException : Exception
    {
        public RuntimeWrappedException(object wrapped) { WrappedException = wrapped; }
        public object WrappedException { get; }
    }
}

namespace System.Text
{
    public enum NormalizationForm
    {
        FormC = 1,
        FormD = 2,
        FormKC = 5,
        FormKD = 6
    }
}

namespace System.Threading
{
    public sealed class Thread
    {
        public static void Sleep(int millisecondsTimeout) { }
        public static void Sleep(TimeSpan timeout) { }
    }
}

namespace System.Dynamic
{
    public sealed class ExpandoObject
    {
    }
}

namespace System.Diagnostics
{
    public class StackTrace
    {
        public StackTrace() { }
        public StackTrace(Exception exception) { }
        public StackTrace(int skipFrames, bool fNeedFileInfo) { }
        public StackTrace(Exception exception, int skipFrames, bool fNeedFileInfo) { }
        public StackFrame GetFrame(int index) => null;
        public int FrameCount => 0;
        public StackFrame[] GetFrames() => new StackFrame[0];
    }

    public class StackFrame
    {
        public int GetFileLineNumber() => 0;
        public string GetFileName() => null;
        public int GetILOffset() => 0;
        public int GetNativeOffset() => 0;
        public MethodBase GetMethod() => null;
    }

    public static class DebuggerPolyfill
    {
        public static void Log(int level, string category, string message) { }
    }
}

namespace System.Reflection
{
    [Flags]
    public enum MemberTypes
    {
        Constructor = 1,
        Event = 2,
        Field = 4,
        Method = 8,
        Property = 16,
        TypeInfo = 32,
        Custom = 64,
        NestedType = 128,
        All = 191
    }

    [Flags]
    public enum BindingFlags
    {
        Default = 0,
        IgnoreCase = 1,
        DeclaredOnly = 2,
        Instance = 4,
        Static = 8,
        Public = 16,
        NonPublic = 32,
        FlattenHierarchy = 64,
        InvokeMethod = 256,
        CreateInstance = 512,
        GetField = 1024,
        SetField = 2048,
        GetProperty = 4096,
        SetProperty = 8192,
        PutDispProperty = 16384,
        PutRefDispProperty = 32768,
        ExactBinding = 65536,
        SuppressChangeType = 131072,
        OptionalParamBinding = 262144,
        IgnoreReturn = 16777216
    }

    public static class TypePolyfill
    {
        public static bool IsAssignableFrom(this Type type, Type other)
        {
            if (other == null) return false;
            return type.GetTypeInfo().IsAssignableFrom(other.GetTypeInfo());
        }

        public static bool IsClass(this Type t) => t.GetTypeInfo().IsClass;
        public static bool IsInterface(this Type t) => t.GetTypeInfo().IsInterface;
        public static bool IsEnum(this Type t) => t.GetTypeInfo().IsEnum;
        public static bool IsValueType(this Type t) => t.GetTypeInfo().IsValueType;
        public static bool IsAbstract(this Type t) => t.GetTypeInfo().IsAbstract;
        public static bool ContainsGenericParameters(this Type t) => t.GetTypeInfo().ContainsGenericParameters;
        public static Type BaseType(this Type t) => t.GetTypeInfo().BaseType;
        public static Type[] GetGenericArguments(this Type t) => t.GetTypeInfo().GenericTypeArguments;
        public static Type[] GetInterfaces(this Type t) => t.GetTypeInfo().ImplementedInterfaces.ToArray();

        public static Type GetInterface(this Type type, string name)
        {
            foreach (var i in type.GetTypeInfo().ImplementedInterfaces)
                if (i.FullName.Contains(name))
                    return i;
            return null;
        }

        public static TypeCode GetTypeCode(this Type type)
        {
            if (type == null) return TypeCode.Empty;
            var ti = type.GetTypeInfo();
            if (ti.IsClass)
            {
                if (type == typeof(string)) return TypeCode.String;
                return TypeCode.Object;
            }
            if (type == typeof(bool)) return TypeCode.Boolean;
            if (type == typeof(char)) return TypeCode.Char;
            if (type == typeof(sbyte)) return TypeCode.SByte;
            if (type == typeof(byte)) return TypeCode.Byte;
            if (type == typeof(short)) return TypeCode.Int16;
            if (type == typeof(ushort)) return TypeCode.UInt16;
            if (type == typeof(int)) return TypeCode.Int32;
            if (type == typeof(uint)) return TypeCode.UInt32;
            if (type == typeof(long)) return TypeCode.Int64;
            if (type == typeof(ulong)) return TypeCode.UInt64;
            if (type == typeof(float)) return TypeCode.Single;
            if (type == typeof(double)) return TypeCode.Double;
            if (type == typeof(decimal)) return TypeCode.Decimal;
            if (type == typeof(DateTime)) return TypeCode.DateTime;
            return TypeCode.Object;
        }

        public static MethodInfo[] GetMethods(this Type t) => t.GetTypeInfo().DeclaredMethods.ToArray();
        public static FieldInfo[] GetFields(this Type t) => t.GetTypeInfo().DeclaredFields.ToArray();
        public static PropertyInfo[] GetProperties(this Type t) => t.GetTypeInfo().DeclaredProperties.ToArray();
        public static ConstructorInfo[] GetConstructors(this Type t) => t.GetTypeInfo().DeclaredConstructors.ToArray();
        public static MemberInfo[] GetMembers(this Type t)
        {
            var ti = t.GetTypeInfo();
            var list = new List<MemberInfo>();
            list.AddRange(ti.DeclaredConstructors);
            list.AddRange(ti.DeclaredEvents);
            list.AddRange(ti.DeclaredFields);
            list.AddRange(ti.DeclaredMethods);
            list.AddRange(ti.DeclaredProperties);
            return list.ToArray();
        }

        public static ConstructorInfo GetConstructor(this Type t, Type[] types)
        {
            foreach (var c in t.GetTypeInfo().DeclaredConstructors)
            {
                var pars = c.GetParameters();
                if (pars.Length != types.Length) continue;
                bool match = true;
                for (int i = 0; i < pars.Length; i++)
                    if (pars[i].ParameterType != types[i]) { match = false; break; }
                if (match) return c;
            }
            return null;
        }

        public static PropertyInfo GetProperty(this Type t, string name, BindingFlags bindingAttr)
        {
            foreach (var p in t.GetTypeInfo().DeclaredProperties)
            {
                if (p.Name != name) continue;
                bool match = true;
                if ((bindingAttr & BindingFlags.Static) != 0 && p.GetMethod?.IsStatic == false) match = false;
                if ((bindingAttr & BindingFlags.Instance) != 0 && p.GetMethod?.IsStatic == true) match = false;
                if ((bindingAttr & BindingFlags.Public) != 0 && p.GetMethod?.IsPublic == false) match = false;
                if ((bindingAttr & BindingFlags.NonPublic) != 0 && p.GetMethod?.IsPublic == true) match = false;
                if (match) return p;
            }
            return null;
        }

        public static MethodInfo GetMethod(this Type t, string name)
        {
            foreach (var m in t.GetTypeInfo().DeclaredMethods)
                if (m.Name == name) return m;
            return null;
        }

        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingAttr)
        {
            var methods = type.GetTypeInfo().GetDeclaredMethods(name);
            foreach (var m in methods)
            {
                bool match = true;
                if ((bindingAttr & BindingFlags.Static) != 0 && !m.IsStatic) match = false;
                if ((bindingAttr & BindingFlags.Instance) != 0 && m.IsStatic) match = false;
                if ((bindingAttr & BindingFlags.Public) != 0 && !m.IsPublic) match = false;
                if ((bindingAttr & BindingFlags.NonPublic) != 0 && m.IsPublic) match = false;
                if (match) return m;
            }
            return null;
        }

        public static MethodInfo GetMethod(this Type type, string name, Type[] types)
        {
            foreach (var m in type.GetTypeInfo().GetDeclaredMethods(name))
            {
                var pars = m.GetParameters();
                if (pars.Length != types.Length) continue;
                bool match = true;
                for (int i = 0; i < pars.Length; i++)
                    if (pars[i].ParameterType != types[i]) { match = false; break; }
                if (match) return m;
            }
            return null;
        }

        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingAttr, Type[] types)
        {
            foreach (var m in type.GetTypeInfo().GetDeclaredMethods(name))
            {
                bool match = true;
                if ((bindingAttr & BindingFlags.Static) != 0 && !m.IsStatic) match = false;
                if ((bindingAttr & BindingFlags.Instance) != 0 && m.IsStatic) match = false;
                if ((bindingAttr & BindingFlags.Public) != 0 && !m.IsPublic) match = false;
                if ((bindingAttr & BindingFlags.NonPublic) != 0 && m.IsPublic) match = false;
                if (!match) continue;
                var pars = m.GetParameters();
                if (types != null)
                {
                    if (pars.Length != types.Length) continue;
                    for (int i = 0; i < pars.Length; i++)
                        if (pars[i].ParameterType != types[i]) { match = false; break; }
                    if (!match) continue;
                }
                return m;
            }
            return null;
        }

        public static MethodInfo GetMethod(this Type type, string name, BindingFlags bindingAttr, object binder, Type[] types, object[] modifiers)
        {
            return type.GetMethod(name, bindingAttr, types);
        }

        public static MethodInfo[] GetMethods(this Type t, BindingFlags flags)
        {
            var list = new List<MethodInfo>();
            foreach (var m in t.GetTypeInfo().DeclaredMethods)
            {
                if ((flags & BindingFlags.Static) != 0 && !m.IsStatic) continue;
                if ((flags & BindingFlags.Instance) != 0 && m.IsStatic) continue;
                if ((flags & BindingFlags.Public) != 0 && !m.IsPublic) continue;
                if ((flags & BindingFlags.NonPublic) != 0 && m.IsPublic) continue;
                list.Add(m);
            }
            return list.ToArray();
        }

        public static FieldInfo[] GetFields(this Type t, BindingFlags flags)
        {
            var list = new List<FieldInfo>();
            foreach (var f in t.GetTypeInfo().DeclaredFields)
            {
                if ((flags & BindingFlags.Static) != 0 && !f.IsStatic) continue;
                if ((flags & BindingFlags.Instance) != 0 && f.IsStatic) continue;
                if ((flags & BindingFlags.Public) != 0 && !f.IsPublic) continue;
                if ((flags & BindingFlags.NonPublic) != 0 && f.IsPublic) continue;
                list.Add(f);
            }
            return list.ToArray();
        }

        public static PropertyInfo[] GetProperties(this Type t, BindingFlags flags)
        {
            var list = new List<PropertyInfo>();
            foreach (var p in t.GetTypeInfo().DeclaredProperties)
            {
                bool match = true;
                if ((flags & BindingFlags.Static) != 0 && p.GetMethod?.IsStatic == false) match = false;
                if ((flags & BindingFlags.Instance) != 0 && p.GetMethod?.IsStatic == true) match = false;
                if ((flags & BindingFlags.Public) != 0 && p.GetMethod?.IsPublic == false) match = false;
                if ((flags & BindingFlags.NonPublic) != 0 && p.GetMethod?.IsPublic == true) match = false;
                if (match) list.Add(p);
            }
            return list.ToArray();
        }

        public static ConstructorInfo[] GetConstructors(this Type t, BindingFlags flags)
        {
            var list = new List<ConstructorInfo>();
            foreach (var c in t.GetTypeInfo().DeclaredConstructors)
            {
                if ((flags & BindingFlags.Static) != 0 && !c.IsStatic) continue;
                if ((flags & BindingFlags.Instance) != 0 && c.IsStatic) continue;
                if ((flags & BindingFlags.Public) != 0 && !c.IsPublic) continue;
                if ((flags & BindingFlags.NonPublic) != 0 && c.IsPublic) continue;
                list.Add(c);
            }
            return list.ToArray();
        }

        public static ConstructorInfo GetConstructor(this Type t, BindingFlags flags, Type[] types)
        {
            foreach (var c in t.GetTypeInfo().DeclaredConstructors)
            {
                bool match = true;
                if ((flags & BindingFlags.Static) != 0 && !c.IsStatic) match = false;
                if ((flags & BindingFlags.Instance) != 0 && c.IsStatic) match = false;
                if ((flags & BindingFlags.Public) != 0 && !c.IsPublic) match = false;
                if ((flags & BindingFlags.NonPublic) != 0 && c.IsPublic) match = false;
                if (!match) continue;
                var pars = c.GetParameters();
                if (pars.Length != types.Length) continue;
                for (int i = 0; i < pars.Length; i++)
                    if (pars[i].ParameterType != types[i]) { match = false; break; }
                if (match) return c;
            }
            return null;
        }

        public static ConstructorInfo GetConstructor(this Type t, BindingFlags bindingAttr, Type[] types, object[] modifiers) => t.GetConstructor(bindingAttr, types);

        public static FieldInfo GetField(this Type t, string name)
        {
            foreach (var f in t.GetTypeInfo().DeclaredFields)
                if (f.Name == name) return f;
            return null;
        }

        public static FieldInfo GetField(this Type type, string name, BindingFlags bindingAttr)
        {
            var fields = type.GetTypeInfo().DeclaredFields;
            foreach (var f in fields)
            {
                if (f.Name != name) continue;
                bool match = true;
                if ((bindingAttr & BindingFlags.Static) != 0 && !f.IsStatic) match = false;
                if ((bindingAttr & BindingFlags.Instance) != 0 && f.IsStatic) match = false;
                if ((bindingAttr & BindingFlags.Public) != 0 && !f.IsPublic) match = false;
                if ((bindingAttr & BindingFlags.NonPublic) != 0 && f.IsPublic) match = false;
                if (match) return f;
            }
            return null;
        }
    }

    public static class PropertyInfoPolyfill
    {
        public static MethodInfo GetGetMethod(this PropertyInfo p) => p.DeclaringType?.GetTypeInfo().GetDeclaredMethod("get_" + p.Name);
        public static MethodInfo GetSetMethod(this PropertyInfo p) => p.DeclaringType?.GetTypeInfo().GetDeclaredMethod("set_" + p.Name);
        public static MethodInfo GetGetMethod(this PropertyInfo p, bool nonPublic) => p.GetGetMethod();
        public static MethodInfo GetSetMethod(this PropertyInfo p, bool nonPublic) => p.GetSetMethod();
    }

    public static class EventInfoPolyfill
    {
        public static MethodInfo GetAddMethod(this EventInfo e) => e.DeclaringType?.GetTypeInfo().GetDeclaredMethod("add_" + e.Name);
        public static MethodInfo GetAddMethod(this EventInfo e, bool nonPublic) => e.GetAddMethod();
    }

    public static class MemberInfoPolyfill
    {
        public static MemberTypes GetMemberType(this MemberInfo m)
        {
            if (m is ConstructorInfo) return MemberTypes.Constructor;
            if (m is EventInfo) return MemberTypes.Event;
            if (m is FieldInfo) return MemberTypes.Field;
            if (m is MethodInfo) return MemberTypes.Method;
            if (m is TypeInfo) return MemberTypes.TypeInfo;
            if (m is PropertyInfo) return MemberTypes.Property;
            return MemberTypes.Custom;
        }
    }

}

namespace Microsoft.CSharp.RuntimeBinder
{
    /*[EditorBrowsable(EditorBrowsableState.Never)]
    [Flags]
    public enum CSharpArgumentInfoFlags
    {
        None = 0,
        UseCompileTimeType = 1,
        Constant = 2,
        NamedArgument = 4,
        IsRef = 8,
        IsOut = 16,
        IsStaticType = 32
    }

    /*[EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class CSharpArgumentInfo
    {
        private CSharpArgumentInfoFlags _flags;
        private string _name;

        public CSharpArgumentInfo(CSharpArgumentInfoFlags flags, string name)
        {
            _flags = flags;
            _name = name;
        }

        public static CSharpArgumentInfo Create(CSharpArgumentInfoFlags flags, string name)
            => new CSharpArgumentInfo(flags, name);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Flags]
    public enum CSharpBinderFlags
    {
        None = 0,
        CheckedContext = 1,
        InvokeSimpleName = 2,
        InvokeSpecialName = 4,
        BinaryOperationLogical = 8,
        ConvertExplicit = 16,
        ConvertArrayIndex = 32,
        ResultIndexed = 64,
        ValueFromCompoundAssignment = 128,
        ResultDiscarded = 256
    }

    /*[EditorBrowsable(EditorBrowsableState.Never)]
    public static class Binder
    {
        public static CallSiteBinder BinaryOperation(CSharpBinderFlags flags, ExpressionType operation, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo)
            => new DynamicMetaObjectBinder(flags, operation, context, argumentInfo);
        public static CallSiteBinder Convert(CSharpBinderFlags flags, Type type, Type context);
        public static CallSiteBinder GetIndex(CSharpBinderFlags flags, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder GetMember(CSharpBinderFlags flags, string name, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder Invoke(CSharpBinderFlags flags, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder InvokeConstructor(CSharpBinderFlags flags, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder InvokeMember(CSharpBinderFlags flags, string name, IEnumerable<Type> typeArguments, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder IsEvent(CSharpBinderFlags flags, string name, Type context);
        public static CallSiteBinder SetIndex(CSharpBinderFlags flags, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder SetMember(CSharpBinderFlags flags, string name, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
        public static CallSiteBinder UnaryOperation(CSharpBinderFlags flags, ExpressionType operation, Type context, IEnumerable<CSharpArgumentInfo> argumentInfo);
    }*/
}
