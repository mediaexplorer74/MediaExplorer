using System;

namespace NiL.JS.Core.Interop;

/// <summary>
/// ”казывает на необходимость учитывать член при создании представител€ в среде выполнени€ сценари€ 
/// вне зависимости от модификатора доступа
/// </summary>
[Serializable]
[AttributeUsage(AttributeTargets.All
, AllowMultiple = false, Inherited = true)]
public
sealed class ForceUseAttribute : Attribute
{
}
