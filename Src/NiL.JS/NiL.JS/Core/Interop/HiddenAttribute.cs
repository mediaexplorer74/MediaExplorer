using System;

namespace NiL.JS.Core.Interop;

/// <summary>
/// „лен, помеченный данным аттрибутом, не будет доступен из сценари€.
/// </summary>
[Serializable]
[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
public sealed class HiddenAttribute : Attribute
{
}
