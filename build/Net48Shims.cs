// 仅在 net48 目标下编译（由 Directory.Build.props 引入）。
// 目的：补齐 C# 9/11 语言特性在 .NET Framework 引用程序集中缺失的编译器内省类型，
// 使 init、record、required 成员在 net48 下可用，从而与 net8.0-windows 目标保持同一份源码。
// 注意：这些类型在 net8 中已由运行时提供，因此本文件不得在 net8 目标编译。

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// 编译器内省类型：支撑 init 访问器与 record 位置参数构造。
    /// </summary>
    internal static class IsExternalInit
    {
    }

    /// <summary>
    /// 编译器内省特性：标记为 required 的成员必须由对象初始化器显式赋值。
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        public string FeatureName { get; }
    }

    /// <summary>
    /// 编译器内省特性：标记 required 成员集合。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// 编译器内省特性：允许派生类型沿用基类的 required 成员集合。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute
    {
    }

    /// <summary>
    /// 可空性分析：方法返回指定值时，参数保证非空。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }

        public bool ReturnValue { get; }
    }

    /// <summary>
    /// 可空性分析：方法返回指定值时，参数可能为 null。
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue)
        {
            ReturnValue = returnValue;
        }

        public bool ReturnValue { get; }
    }

    /// <summary>
    /// 可空性分析：方法正常返回后，指定成员保证非空。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Property,
        AllowMultiple = true,
        Inherited = false)]
    internal sealed class MemberNotNullAttribute : Attribute
    {
        public MemberNotNullAttribute(string member)
        {
            Members = new[] { member };
        }

        public MemberNotNullAttribute(params string[] members)
        {
            Members = members;
        }

        public string[] Members { get; }
    }

    /// <summary>
    /// 可空性分析：方法不会正常返回。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class DoesNotReturnAttribute : Attribute
    {
    }
}