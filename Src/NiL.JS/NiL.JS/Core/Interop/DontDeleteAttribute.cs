using System;

namespace NiL.JS.Core.Interop;

/// <summary>
/// „лен, помеченный данным аттрибутом, не будет удал€тьс€ оператором "delete".
/// </summary>
[Serializable]
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DoNotDeleteAttribute : Attribute
{
}
